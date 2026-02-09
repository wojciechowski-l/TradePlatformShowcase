using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Testcontainers.MsSql;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Worker.Handlers;
using Wolverine;

namespace TradePlatform.Tests.Worker
{
    public class TransactionCreatedHandlerTests(WorkerDatabaseFixture fixture) : IClassFixture<WorkerDatabaseFixture>
    {
        private readonly WorkerDatabaseFixture _fixture = fixture;

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

            var mockBus = new Mock<IMessageBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();

            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            await TransactionCreatedHandler.Handle(evt, context, mockBus.Object, mockLogger.Object);

            context.ChangeTracker.Clear();
            var updatedTx = await context.Transactions.FindAsync(
                [txId],
                TestContext.Current.CancellationToken
            );

            Assert.NotNull(updatedTx);
            Assert.Equal(TransactionStatus.Processed, updatedTx.Status);

            mockBus.Verify(
                m => m.PublishAsync(
                    It.Is<TransactionUpdateDto>(u =>
                        u.TransactionId == txId &&
                        u.Status == TransactionStatus.Processed &&
                        u.AccountId == srcAccId),
                    It.IsAny<DeliveryOptions>()
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

            var mockBus = new Mock<IMessageBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();
            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            await TransactionCreatedHandler.Handle(evt, context, mockBus.Object, mockLogger.Object);

            mockBus.Verify(m => m.PublishAsync(It.IsAny<object>(), It.IsAny<DeliveryOptions>()), Times.Never);
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