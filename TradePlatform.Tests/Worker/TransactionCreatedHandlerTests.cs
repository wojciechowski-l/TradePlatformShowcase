using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Rebus.Bus;
using Testcontainers.MsSql;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Worker.Handlers;

namespace TradePlatform.Tests.Worker
{
    public class TransactionCreatedHandlerTests(WorkerDatabaseFixture fixture) : IClassFixture<WorkerDatabaseFixture>
    {
        private readonly WorkerDatabaseFixture _fixture = fixture;

        private static Mock<ITransactionScopeManager> CreateMockTransactionScopeManager()
        {
            var mock = new Mock<ITransactionScopeManager>();
            mock.Setup(m => m.ExecuteInTransactionAsync(It.IsAny<Func<Task>>()))
                .Returns((Func<Task> action) => action());
            return mock;
        }

        [Fact]
        public async Task Handle_Should_Process_Transaction_And_Publish_Notification()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var userId = Guid.NewGuid().ToString();
            var srcAccId = $"SRC_{Guid.NewGuid()}";
            var tgtAccId = $"TGT_{Guid.NewGuid()}";
            var txId = Guid.NewGuid();

            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"User_{Guid.NewGuid()}",
                Email = $"test_{Guid.NewGuid()}@example.com",
                FullName = "Test User"
            };

            var srcAccount = new Account
            {
                Id = srcAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            var tgtAccount = new Account
            {
                Id = tgtAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            context.Users.Add(user);
            context.Accounts.AddRange(srcAccount, tgtAccount);

            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                SourceAccountId = srcAccId,
                TargetAccountId = tgtAccId,
                Amount = 50,
                Currency = Currency.FromCode("USD"),
                Status = TransactionStatus.Pending
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();
            var mockTransactionManager = CreateMockTransactionScopeManager();

            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                mockTransactionManager.Object,
                mockLogger.Object);

            await handler.Handle(evt);

            context.ChangeTracker.Clear();
            var updatedTx = await context.Transactions.FindAsync(
                [txId],
                TestContext.Current.CancellationToken
            );

            Assert.NotNull(updatedTx);
            Assert.Equal(TransactionStatus.Processed, updatedTx.Status);

            mockBus.Verify(
                m => m.Publish(
                    It.Is<TransactionProcessedEvent>(e =>
                        e.TransactionId == txId &&
                        e.Status == TransactionStatus.Processed &&
                        e.AccountId == srcAccId),
                    It.IsAny<IDictionary<string, string>>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task Handle_Should_Be_Idempotent_If_Already_Processed()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var userId = Guid.NewGuid().ToString();
            var srcAccId = $"SRC_{Guid.NewGuid()}";
            var tgtAccId = $"TGT_{Guid.NewGuid()}";
            var txId = Guid.NewGuid();

            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"User_{Guid.NewGuid()}",
                Email = $"test_{Guid.NewGuid()}@example.com",
                FullName = "Test User"
            };

            var srcAccount = new Account
            {
                Id = srcAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            var tgtAccount = new Account
            {
                Id = tgtAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            context.Users.Add(user);
            context.Accounts.AddRange(srcAccount, tgtAccount);

            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                SourceAccountId = srcAccId,
                TargetAccountId = tgtAccId,
                Amount = 50,
                Currency = Currency.FromCode("USD"),
                Status = TransactionStatus.Processed
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();
            var mockTransactionManager = CreateMockTransactionScopeManager();

            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                mockTransactionManager.Object,
                mockLogger.Object);

            await handler.Handle(evt);

            mockBus.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
        }
    }

    public class WorkerDatabaseFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

        public async ValueTask InitializeAsync()
        {
            await _dbContainer.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _dbContainer.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        public TradeContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;

            return new TradeContext(options);
        }
    }
}