using Rebus.Bus;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Infrastructure.Services
{
    public class TransactionService(ITradeContext context, IBus bus) : ITransactionService
    {
        private readonly ITradeContext _context = context;
        private readonly IBus _bus = bus;

        public async Task<CreateTransactionResult> CreateTransactionAsync(TransactionDto request)
        {
            var transaction = new TransactionRecord
            {
                Id = Guid.NewGuid(),
                SourceAccountId = request.SourceAccountId,
                TargetAccountId = request.TargetAccountId,
                Amount = request.Amount,
                Currency = Currency.FromCode(request.Currency),
                Status = TransactionStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            var eventPayload = new TransactionCreatedEvent(
                transaction.Id,
                transaction.SourceAccountId,
                transaction.TargetAccountId,
                transaction.Amount,
                transaction.Currency.Code
            );

            _context.Transactions.Add(transaction);

            await _context.SaveChangesAsync();

            await _bus.Send(eventPayload);

            return new CreateTransactionResult
            {
                TransactionId = transaction.Id,
                Status = TransactionStatus.Pending
            };
        }
    }
}