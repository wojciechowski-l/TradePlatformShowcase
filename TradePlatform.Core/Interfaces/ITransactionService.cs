using TradePlatform.Core.DTOs;

namespace TradePlatform.Core.Interfaces
{
    public interface ITransactionService
    {
        Task<CreateTransactionResult> CreateTransactionAsync(
            TransactionDto request,
            string? idempotencyKey,
            string userId,
            CancellationToken cancellationToken = default);
    }
}