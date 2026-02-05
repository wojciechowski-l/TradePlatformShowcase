using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Worker.Handlers;
using Wolverine;

namespace TradePlatform.Tests.Worker
{
    public class TransactionCreatedHandlerTests
    {
        [Fact]
        public async Task Handle_Should_Process_Transaction_And_Publish_Notification()
        {
            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new TradeContext(options);

            var txId = Guid.NewGuid();
            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                SourceAccountId = "SRC",
                TargetAccountId = "TGT",
                Amount = 50,
                Currency = "USD",
                Status = TransactionStatus.Pending
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IMessageBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();

            var evt = new TransactionCreatedEvent(txId, "SRC", "TGT", 50, "USD");

            await TransactionCreatedHandler.Handle(evt, context, mockBus.Object, mockLogger.Object);

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
                        u.AccountId == "SRC"),
                    It.IsAny<DeliveryOptions>()
                ),
                Times.Once
            );
        }

        [Fact]
        public async Task Handle_Should_Be_Idempotent_If_Already_Processed()
        {
            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            using var context = new TradeContext(options);
            var txId = Guid.NewGuid();
            context.Transactions.Add(new TransactionRecord
            {
                Id = txId,
                Status = TransactionStatus.Processed
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var mockBus = new Mock<IMessageBus>();
            var mockLogger = new Mock<ILogger<TransactionCreatedHandler>>();
            var evt = new TransactionCreatedEvent(txId, "SRC", "TGT", 50, "USD");

            await TransactionCreatedHandler.Handle(evt, context, mockBus.Object, mockLogger.Object);

            mockBus.Verify(m => m.PublishAsync(It.IsAny<object>(), It.IsAny<DeliveryOptions>()), Times.Never);
        }
    }
}