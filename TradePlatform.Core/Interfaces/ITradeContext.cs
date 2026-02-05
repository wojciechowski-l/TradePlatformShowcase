using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TradePlatform.Core.Entities;

namespace TradePlatform.Core.Interfaces
{
    public interface ITradeContext
    {
        DbSet<TransactionRecord> Transactions { get; }
        DbSet<Account> Accounts { get; }

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}