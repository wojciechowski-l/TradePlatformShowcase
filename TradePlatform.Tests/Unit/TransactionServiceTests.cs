using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Rebus.Bus;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Services;

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
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _);
            var mockBus = new Mock<IBus>();

            var service = new TransactionService(mockContext.Object, mockBus.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_001",
                TargetAccountId = "ACC_002",
                Amount = 100m,
                Currency = "USD"
            };

            var result = await service.CreateTransactionAsync(request);

            Assert.NotNull(result);
            Assert.Equal(TransactionStatus.Pending, result.Status);

            transactionsDbSetMock.Verify(
                m => m.Add(It.Is<TransactionRecord>(t =>
                    t.SourceAccountId == request.SourceAccountId &&
                    t.Amount == request.Amount &&
                    t.Status == TransactionStatus.Pending)),
                Times.Once
            );

            mockBus.Verify(
                m => m.Send(
                    It.Is<TransactionCreatedEvent>(e =>
                        e.SourceAccountId == request.SourceAccountId &&
                        e.Amount == request.Amount),
                    It.IsAny<IDictionary<string, string>>()
                ),
                Times.Once
            );

            mockContext.Verify(c => c.SaveChangesAsync(default), Times.Once);
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_Rollback_On_Error()
        {
            var mockContext = CreateMockContext(out _, out _);
            var mockBus = new Mock<IBus>();

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

            await Assert.ThrowsAsync<DbUpdateException>(
                async () => await service.CreateTransactionAsync(request)
            );

            mockBus.Verify(
                m => m.Send(
                    It.IsAny<TransactionCreatedEvent>(),
                    It.IsAny<IDictionary<string, string>>()
                ),
                Times.Never
            );
        }
    }
}