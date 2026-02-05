using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Services;
using Wolverine;

namespace TradePlatform.Tests.Unit
{
    public class TransactionServiceTests
    {
        private static Mock<ITradeContext> CreateMockContext(
            out Mock<DbSet<TransactionRecord>> transactionsDbSetMock,
            out Mock<IDbContextTransaction> transactionMock
        )
        {
            var mockContext = new Mock<ITradeContext>();

            transactionsDbSetMock = new Mock<DbSet<TransactionRecord>>();
            transactionMock = new Mock<IDbContextTransaction>();

            mockContext.Setup(c => c.Transactions).Returns(transactionsDbSetMock.Object);

            mockContext
                .Setup(c => c.BeginTransactionAsync(default))
                .ReturnsAsync(transactionMock.Object);

            mockContext.Setup(c => c.SaveChangesAsync(default)).ReturnsAsync(1);

            return mockContext;
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_Create_Transaction_And_Publish_Event()
        {
            // Arrange
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _);
            var mockBus = new Mock<IMessageBus>();

            var service = new TransactionService(mockContext.Object, mockBus.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_001",
                TargetAccountId = "ACC_002",
                Amount = 100m,
                Currency = "USD"
            };

            // Act
            var result = await service.CreateTransactionAsync(request);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(TransactionStatus.Pending, result.Status);

            transactionsDbSetMock.Verify(
                m => m.Add(It.Is<TransactionRecord>(t =>
                    t.SourceAccountId == request.SourceAccountId &&
                    t.Amount == request.Amount &&
                    t.Status == TransactionStatus.Pending)),
                Times.Once
            );

            // Verify Message Published to Wolverine
            // Removed CancellationToken argument to match the signature
            mockBus.Verify(
                m => m.PublishAsync(
                    It.Is<TransactionCreatedEvent>(e =>
                        e.SourceAccountId == request.SourceAccountId &&
                        e.Amount == request.Amount),
                    It.IsAny<DeliveryOptions>()
                ),
                Times.Once
            );

            // 3. Verify Changes Saved
            mockContext.Verify(c => c.SaveChangesAsync(default), Times.Once);
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_Rollback_On_Error()
        {
            // Arrange
            var mockContext = CreateMockContext(out _, out _);
            var mockBus = new Mock<IMessageBus>();

            mockContext
                .Setup(c => c.SaveChangesAsync(default))
                .ThrowsAsync(new DbUpdateException("Database constraint violation"));

            var service = new TransactionService(mockContext.Object, mockBus.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_007",
                TargetAccountId = "ACC_008",
                Amount = 1000m,
                Currency = "USD"
            };

            // Act & Assert
            await Assert.ThrowsAsync<DbUpdateException>(
                async () => await service.CreateTransactionAsync(request)
            );

            // Verify message was attempted
            // Note: We use It.IsAny<TransactionCreatedEvent> because PublishAsync is generic <T> 
            // and the service calls it with that specific type.
            mockBus.Verify(
                m => m.PublishAsync(
                    It.IsAny<TransactionCreatedEvent>(),
                    It.IsAny<DeliveryOptions>()
                ),
                Times.Once
            );
        }
    }
}