using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
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
            out Mock<DbSet<OutboxMessage>> outboxDbSetMock,
            out Mock<IDbContextTransaction> transactionMock
        )
        {
            var mockContext = new Mock<ITradeContext>();

            transactionsDbSetMock = new Mock<DbSet<TransactionRecord>>();
            outboxDbSetMock = new Mock<DbSet<OutboxMessage>>();
            transactionMock = new Mock<IDbContextTransaction>();

            mockContext.Setup(c => c.Transactions).Returns(transactionsDbSetMock.Object);

            mockContext.Setup(c => c.OutboxMessages).Returns(outboxDbSetMock.Object);

            mockContext
                .Setup(c => c.BeginTransactionAsync(default))
                .ReturnsAsync(transactionMock.Object);

            mockContext.Setup(c => c.SaveChangesAsync(default)).ReturnsAsync(1);

            return mockContext;
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_CreateTransaction_WithCorrectProperties()
        {
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _, out _);

            var service = new TransactionService(mockContext.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_001",
                TargetAccountId = "ACC_002",
                Amount = 250.50m,
                Currency = "USD"
            };

            var result = await service.CreateTransactionAsync(request);

            transactionsDbSetMock.Verify(
                m =>
                    m.Add(
                        It.Is<TransactionRecord>(
                            t =>
                                t.SourceAccountId == "ACC_001"
                                && t.TargetAccountId == "ACC_002"
                                && t.Amount == 250.50m
                                && t.Currency == "USD"
                                && t.Status == TransactionStatus.Pending
                        )
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_CreateOutboxMessage_OnlyAfterTransactionCreated()
        {
            var mockContext = CreateMockContext(out _, out var outboxDbSetMock, out _);

            var service = new TransactionService(mockContext.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_003",
                TargetAccountId = "ACC_004",
                Amount = 100m,
                Currency = "EUR"
            };

            await service.CreateTransactionAsync(request);

            outboxDbSetMock.Verify(
                m =>
                    m.Add(
                        It.Is<OutboxMessage>(
                            o => o.Type == "TransactionCreated" && !string.IsNullOrEmpty(o.Payload)
                        )
                    ),
                Times.Once
            );

            mockContext.Verify(c => c.SaveChangesAsync(default), Times.Once);
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_UseAtomicTransaction()
        {
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _, out _);

            var mockTransaction = new Mock<IDbContextTransaction>();

            mockContext
                .Setup(c => c.BeginTransactionAsync(default))
                .ReturnsAsync(mockTransaction.Object);

            var service = new TransactionService(mockContext.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_005",
                TargetAccountId = "ACC_006",
                Amount = 500m,
                Currency = "GBP"
            };

            await service.CreateTransactionAsync(request);

            mockContext.Verify(c => c.BeginTransactionAsync(default), Times.Once);
            mockContext.Verify(c => c.SaveChangesAsync(default), Times.Once);
            mockTransaction.Verify(t => t.CommitAsync(default), Times.Once);
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_PropagateException_WhenSaveFails()
        {
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _, out _);

            mockContext
                .Setup(c => c.SaveChangesAsync(default))
                .ThrowsAsync(new DbUpdateException("Database constraint violation"));

            var service = new TransactionService(mockContext.Object);

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
        }

        [Fact]
        public async Task CreateTransactionAsync_Should_SetPendingStatus()
        {
            var mockContext = CreateMockContext(out var transactionsDbSetMock, out _, out _);

            var service = new TransactionService(mockContext.Object);

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_009",
                TargetAccountId = "ACC_010",
                Amount = 75.25m,
                Currency = "CAD"
            };

            var result = await service.CreateTransactionAsync(request);

            Assert.Equal(TransactionStatus.Pending, result.Status);

            transactionsDbSetMock.Verify(
                m => m.Add(It.Is<TransactionRecord>(t => t.Status == TransactionStatus.Pending)),
                Times.Once
            );
        }
    }
}
