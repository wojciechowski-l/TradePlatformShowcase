using TradePlatform.Core.DTOs;

namespace TradePlatform.Core.Interfaces
{
    public interface ITransactionService
    {
        Task<CreateTransactionResult> CreateTransactionAsync(TransactionDto request, CancellationToken cancellationToken = default);
    }
}