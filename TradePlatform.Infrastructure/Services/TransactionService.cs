using System.Text.Json;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Infrastructure.Services
{
    public class TransactionService(ITradeContext context) : ITransactionService
    {
        private readonly ITradeContext _context = context;

        public async Task<CreateTransactionResult> CreateTransactionAsync(TransactionDto request)
        {
            var transaction = new TransactionRecord
            {
                Id = Guid.NewGuid(),
                SourceAccountId = request.SourceAccountId,
                TargetAccountId = request.TargetAccountId,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = TransactionStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            var outboxMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "TransactionCreated",
                Payload = JsonSerializer.Serialize(transaction.Id),
                CreatedAtUtc = DateTime.UtcNow
            };

            using var dbTx = await _context.BeginTransactionAsync();

            _context.Transactions.Add(transaction);
            _context.OutboxMessages.Add(outboxMessage);

            await _context.SaveChangesAsync();
            await dbTx.CommitAsync();

            return new CreateTransactionResult
            {
                TransactionId = transaction.Id,
                Status = TransactionStatus.Pending
            };
        }
    }
}