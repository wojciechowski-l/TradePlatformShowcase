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

        private static readonly string ConsumerName =
            $"{typeof(TransactionCreatedHandler).FullName}:{typeof(TransactionCreatedEvent).FullName}";

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
            await EnsureOutboxTableAsync(context);

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

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IBus>();
            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

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
            var source = await context.Accounts.FindAsync([srcAccId], TestContext.Current.CancellationToken);
            var target = await context.Accounts.FindAsync([tgtAccId], TestContext.Current.CancellationToken);

            Assert.NotNull(updatedTx);
            Assert.Equal(TransactionStatus.Processed, updatedTx.Status);
            Assert.NotNull(source);
            Assert.Equal(450m, source.Balance);
            Assert.NotNull(target);
            Assert.Equal(60m, target.Balance);

            mockBus.Verify(
                m => m.Publish(
                    It.Is<TransactionStatusChangedEvent>(e =>
                        e.TransactionId == txId &&
                        e.CurrentStatus == TransactionStatus.Processed &&
                        e.SourceAccountId == srcAccId &&
                        e.TargetAccountId == tgtAccId &&
                        e.Amount == 50m &&
                        e.Currency == "USD"),
                    It.IsAny<IDictionary<string, string>>()),
                Times.Once);

            mockBus.Verify(
                m => m.Publish(It.IsAny<TransactionStatusChangedEvent>(), It.IsAny<IDictionary<string, string>>()),
                Times.Exactly(3));
        }

        [Fact]
        public async Task Handle_Should_Be_Idempotent_If_Already_Processed()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await EnsureOutboxTableAsync(context);

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
                Status = TransactionStatus.Processed
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

            var mockBus = new Mock<IBus>();
            var handler = new TransactionCreatedHandler(
                context,
                mockBus.Object,
                CreateMockTransactionScopeManager().Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            await handler.Handle(new TransactionCreatedEvent(txId, srcAccId, tgtAccId, 50m, "USD"));

            mockBus.Verify(m => m.Publish(It.IsAny<object>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
        }

        [Fact]
        public async Task Handle_Should_Fail_Transaction_When_Source_Funds_Are_Insufficient()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await EnsureOutboxTableAsync(context);

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

            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(Guid.NewGuid().ToString("N"));
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

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
            await EnsureOutboxTableAsync(context);

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
                Consumer = ConsumerName,
                ProcessedAtUtc = DateTime.UtcNow
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

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

        [Fact]
        public async Task Handle_Should_Roll_Back_Inbox_And_State_When_Publish_Fails_Then_Succeed_On_Retry()
        {
            var txId = Guid.NewGuid();
            var messageId = Guid.NewGuid().ToString("N");
            var sourceAccountId = $"SRC_{Guid.NewGuid()}";
            var targetAccountId = $"TGT_{Guid.NewGuid()}";

            await SeedPendingTransactionAsync(txId, sourceAccountId, targetAccountId, 50m, 500m, 10m);

            await using (var failingContext = _fixture.CreateContext())
            {
                await EnsureOutboxTableAsync(failingContext);

                var failingBus = new Mock<IBus>();
                failingBus
                    .Setup(bus => bus.Publish(It.IsAny<TransactionStatusChangedEvent>(), It.IsAny<IDictionary<string, string>>()))
                    .ThrowsAsync(new InvalidOperationException("publish failed"));

                var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
                messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);
                messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

                var handler = new TransactionCreatedHandler(
                    failingContext,
                    failingBus.Object,
                    new RebusSqlTransactionScopeManager(failingContext),
                    new SqlMessageInbox(),
                    messageMetadataAccessor.Object,
                    Mock.Of<ILogger<TransactionCreatedHandler>>());

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    handler.Handle(new TransactionCreatedEvent(txId, sourceAccountId, targetAccountId, 50m, "USD")));
            }

            await using (var verificationContext = _fixture.CreateContext())
            {
                var transaction = await verificationContext.Transactions.FindAsync([txId], TestContext.Current.CancellationToken);
                var source = await verificationContext.Accounts.FindAsync([sourceAccountId], TestContext.Current.CancellationToken);
                var target = await verificationContext.Accounts.FindAsync([targetAccountId], TestContext.Current.CancellationToken);

                Assert.NotNull(transaction);
                Assert.Equal(TransactionStatus.Pending, transaction.Status);
                Assert.NotNull(source);
                Assert.Equal(500m, source.Balance);
                Assert.NotNull(target);
                Assert.Equal(10m, target.Balance);
                Assert.False(await verificationContext.InboxMessages.AnyAsync(
                    message => message.MessageId == messageId && message.Consumer == ConsumerName,
                    TestContext.Current.CancellationToken));
            }

            await using (var retryContext = _fixture.CreateContext())
            {
                var retryBus = new Mock<IBus>();
                var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
                messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);
                messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(2);

                var handler = new TransactionCreatedHandler(
                    retryContext,
                    retryBus.Object,
                    new RebusSqlTransactionScopeManager(retryContext),
                    new SqlMessageInbox(),
                    messageMetadataAccessor.Object,
                    Mock.Of<ILogger<TransactionCreatedHandler>>());

                await handler.Handle(new TransactionCreatedEvent(txId, sourceAccountId, targetAccountId, 50m, "USD"));
            }

            await using (var verificationContext = _fixture.CreateContext())
            {
                var transaction = await verificationContext.Transactions.FindAsync([txId], TestContext.Current.CancellationToken);
                var source = await verificationContext.Accounts.FindAsync([sourceAccountId], TestContext.Current.CancellationToken);
                var target = await verificationContext.Accounts.FindAsync([targetAccountId], TestContext.Current.CancellationToken);

                Assert.NotNull(transaction);
                Assert.Equal(TransactionStatus.Processed, transaction.Status);
                Assert.NotNull(source);
                Assert.Equal(450m, source.Balance);
                Assert.NotNull(target);
                Assert.Equal(60m, target.Balance);
                Assert.True(await verificationContext.InboxMessages.AnyAsync(
                    message => message.MessageId == messageId && message.Consumer == ConsumerName,
                    TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task Handle_Should_Process_Only_Once_When_Same_Message_Is_Delivered_Concurrently()
        {
            var txId = Guid.NewGuid();
            var messageId = Guid.NewGuid().ToString("N");
            var sourceAccountId = $"SRC_{Guid.NewGuid()}";
            var targetAccountId = $"TGT_{Guid.NewGuid()}";

            await SeedPendingTransactionAsync(txId, sourceAccountId, targetAccountId, 50m, 500m, 10m);

            await using var contextA = _fixture.CreateContext();
            await using var contextB = _fixture.CreateContext();
            await EnsureOutboxTableAsync(contextA);

            var accessorA = new Mock<IMessageMetadataAccessor>();
            accessorA.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);
            accessorA.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

            var accessorB = new Mock<IMessageMetadataAccessor>();
            accessorB.Setup(accessor => accessor.GetCurrentMessageId()).Returns(messageId);
            accessorB.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

            var handlerA = new TransactionCreatedHandler(
                contextA,
                Mock.Of<IBus>(),
                new RebusSqlTransactionScopeManager(contextA),
                new SqlMessageInbox(),
                accessorA.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            var handlerB = new TransactionCreatedHandler(
                contextB,
                Mock.Of<IBus>(),
                new RebusSqlTransactionScopeManager(contextB),
                new SqlMessageInbox(),
                accessorB.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            await Task.WhenAll(
                handlerA.Handle(new TransactionCreatedEvent(txId, sourceAccountId, targetAccountId, 50m, "USD")),
                handlerB.Handle(new TransactionCreatedEvent(txId, sourceAccountId, targetAccountId, 50m, "USD")));

            await using var verificationContext = _fixture.CreateContext();
            var transaction = await verificationContext.Transactions.FindAsync([txId], TestContext.Current.CancellationToken);
            var source = await verificationContext.Accounts.FindAsync([sourceAccountId], TestContext.Current.CancellationToken);
            var target = await verificationContext.Accounts.FindAsync([targetAccountId], TestContext.Current.CancellationToken);
            var inboxEntries = await verificationContext.InboxMessages.CountAsync(
                message => message.MessageId == messageId && message.Consumer == ConsumerName,
                TestContext.Current.CancellationToken);

            Assert.NotNull(transaction);
            Assert.Equal(TransactionStatus.Processed, transaction.Status);
            Assert.NotNull(source);
            Assert.Equal(450m, source.Balance);
            Assert.NotNull(target);
            Assert.Equal(60m, target.Balance);
            Assert.Equal(1, inboxEntries);
        }

        [Fact]
        public async Task Handle_Should_Throw_When_Message_Metadata_Does_Not_Contain_Message_Id()
        {
            using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await EnsureOutboxTableAsync(context);

            var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentMessageId()).Returns((string?)null);
            messageMetadataAccessor.Setup(accessor => accessor.GetCurrentDeliveryCount()).Returns(1);

            var handler = new TransactionCreatedHandler(
                context,
                Mock.Of<IBus>(),
                CreateMockTransactionScopeManager().Object,
                new SqlMessageInbox(),
                messageMetadataAccessor.Object,
                Mock.Of<ILogger<TransactionCreatedHandler>>());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(new TransactionCreatedEvent(Guid.NewGuid(), "ACC-1", "ACC-2", 10m, "USD")));

            Assert.Contains("Missing Rebus message id", exception.Message);
        }

        private async Task SeedPendingTransactionAsync(
            Guid transactionId,
            string sourceAccountId,
            string targetAccountId,
            decimal amount,
            decimal sourceBalance,
            decimal targetBalance)
        {
            await using var context = _fixture.CreateContext();
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await EnsureOutboxTableAsync(context);

            var userId = Guid.NewGuid().ToString();

            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"seed-{Guid.NewGuid()}",
                Email = $"seed-{Guid.NewGuid()}@example.com",
                FullName = "Seed User"
            });

            context.Accounts.AddRange(
                new Account
                {
                    Id = sourceAccountId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = sourceBalance
                },
                new Account
                {
                    Id = targetAccountId,
                    OwnerId = userId,
                    Currency = Currency.FromCode("USD"),
                    Balance = targetBalance
                });

            context.Transactions.Add(new TransactionRecord
            {
                Id = transactionId,
                SourceAccountId = sourceAccountId,
                TargetAccountId = targetAccountId,
                Amount = amount,
                Currency = Currency.FromCode("USD"),
                Status = TransactionStatus.Pending
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        private static async Task EnsureOutboxTableAsync(TradeContext context)
        {
            var createOutboxSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RebusOutbox')
                CREATE TABLE [dbo].[RebusOutbox] (
                    [id] [bigint] IDENTITY(1,1) NOT NULL,
                    [message_id] [nvarchar](200) NOT NULL,
                    [source_queue] [nvarchar](200) NOT NULL,
                    [destination_queue] [nvarchar](200) NOT NULL,
                    [headers] [varbinary](max) NULL,
                    [body] [varbinary](max) NULL,
                    [creation_time] [datetimeoffset](7) NOT NULL,
                    CONSTRAINT [PK_RebusOutbox] PRIMARY KEY CLUSTERED ([id] ASC)
                );";

            await context.Database.ExecuteSqlRawAsync(createOutboxSql, TestContext.Current.CancellationToken);
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
