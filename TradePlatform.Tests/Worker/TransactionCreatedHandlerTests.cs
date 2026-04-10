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
using TradePlatform.Infrastructure.Services;
using TradePlatform.Worker.Handlers;

namespace TradePlatform.Tests.Worker
{
    public class TransactionCreatedHandlerTests(WorkerDatabaseFixture fixture) : IClassFixture<WorkerDatabaseFixture>
    {
        private readonly WorkerDatabaseFixture _fixture = fixture;

        private static Mock<ITransactionScopeManager> CreateMockTransactionScopeManager()
        {
            var mock = new Mock<ITransactionScopeManager>();
            mock.Setup(m => m.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> action, CancellationToken _) => action());
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
                Currency = Currency.FromCode("USD"),
                Balance = 500m
            };

            var tgtAccount = new Account
            {
                Id = tgtAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD"),
                Balance = 10m
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
            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));

            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                mockTransactionManager.Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                mockLogger.Object);

            await handler.Handle(evt);

            context.ChangeTracker.Clear();
            var updatedTx = await context.Transactions.FindAsync(
                [txId],
                TestContext.Current.CancellationToken
            );

            Assert.NotNull(updatedTx);
            Assert.Equal(TransactionStatus.Processed, updatedTx.Status);
            Assert.Equal(450m, (await context.Accounts.FindAsync([srcAccId], TestContext.Current.CancellationToken))!.Balance);
            Assert.Equal(60m, (await context.Accounts.FindAsync([tgtAccId], TestContext.Current.CancellationToken))!.Balance);

            mockBus.Verify(
                m => m.Publish(
                    It.Is<TransactionStatusChangedEvent>(e =>
                        e.TransactionId == txId &&
                        e.CurrentStatus == TransactionStatus.Processed &&
                        e.SourceAccountId == srcAccId &&
                        e.TargetAccountId == tgtAccId &&
                        e.Amount == 50 &&
                        e.Currency == "USD"),
                    It.IsAny<IDictionary<string, string>>()
                ),
                Times.Once
            );

            mockBus.Verify(
                m => m.Publish(
                    It.IsAny<TransactionStatusChangedEvent>(),
                    It.IsAny<IDictionary<string, string>>()),
                Times.Exactly(3));
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
                Currency = Currency.FromCode("USD"),
                Balance = 500m
            };

            var tgtAccount = new Account
            {
                Id = tgtAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD"),
                Balance = 10m
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
            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));

            var evt = new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50, "USD");

            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                mockTransactionManager.Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                mockLogger.Object);

            await handler.Handle(evt);

            mockBus.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
        }

        [Fact]
        public async Task Handle_Should_Fail_Transaction_When_Source_Funds_Are_Insufficient()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var userId = Guid.NewGuid().ToString();
            var srcAccId = $"SRC_{Guid.NewGuid()}";
            var tgtAccId = $"TGT_{Guid.NewGuid()}";
            var txId = Guid.NewGuid();

            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"User_{Guid.NewGuid()}",
                Email = $"test_{Guid.NewGuid()}@example.com",
                FullName = "Test User"
            });

            context.Accounts.AddRange(
                new Account
                {
                    Id = srcAccId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = 25m
                },
                new Account
                {
                    Id = tgtAccId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = 10m
                });

            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                SourceAccountId = srcAccId,
                TargetAccountId = tgtAccId,
                Amount = 50m,
                Currency = Currency.FromCode("USD"),
                Status = TransactionStatus.Pending
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IBus>();
            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));

            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                CreateMockTransactionScopeManager().Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            await handler.Handle(new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50m, "USD"));

            context.ChangeTracker.Clear();

            var updatedTx = await context.Transactions.FindAsync([txId], TestContext.Current.CancellationToken);

            Assert.NotNull(updatedTx);
            Assert.Equal(TransactionStatus.Failed, updatedTx.Status);
            Assert.Equal("Source account has insufficient funds.", updatedTx.FailureReason);

            mockBus.Verify(
                m => m.Publish(
                    It.Is<TransactionStatusChangedEvent>(e =>
                        e.TransactionId == txId &&
                        e.CurrentStatus == TransactionStatus.Failed &&
                        e.FailureReason == "Source account has insufficient funds."),
                    It.IsAny<IDictionary<string, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_Should_Skip_Duplicate_Message_Delivery()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var userId = Guid.NewGuid().ToString();
            var srcAccId = $"SRC_{Guid.NewGuid()}";
            var tgtAccId = $"TGT_{Guid.NewGuid()}";
            var txId = Guid.NewGuid();
            var messageId = Guid.NewGuid().ToString("N");

            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"User_{Guid.NewGuid()}",
                Email = $"test_{Guid.NewGuid()}@example.com",
                FullName = "Test User"
            });

            context.Accounts.AddRange(
                new Account
                {
                    Id = srcAccId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = 500m
                },
                new Account
                {
                    Id = tgtAccId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = 10m
                });

            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                SourceAccountId = srcAccId,
                TargetAccountId = tgtAccId,
                Amount = 50m,
                Currency = Currency.FromCode("USD"),
                Status = TransactionStatus.Pending
            });

            context.InboxMessages.Add(new InboxMessage
            {
                MessageId = messageId,
                Consumer = $"{typeof(TransactionCreatedHandler).FullName}:{typeof(TransactionCreatedEvent).FullName}",
                ProcessedAtUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);

            var mockBus = new Mock<IBus>();
            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                CreateMockTransactionScopeManager().Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            await handler.Handle(new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50m, "USD"));

            context.ChangeTracker.Clear();

            var transaction = await context.Transactions.FindAsync([txId], TestContext.Current.CancellationToken);
            var source = await context.Accounts.FindAsync([srcAccId], TestContext.Current.CancellationToken);
            var target = await context.Accounts.FindAsync([tgtAccId], TestContext.Current.CancellationToken);

            Assert.NotNull(transaction);
            Assert.Equal(TransactionStatus.Pending, transaction.Status);
            Assert.NotNull(source);
            Assert.Equal(500m, source.Balance);
            Assert.NotNull(target);
            Assert.Equal(10m, target.Balance);
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
