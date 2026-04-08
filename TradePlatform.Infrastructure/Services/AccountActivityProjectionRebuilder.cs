using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradePlatform.Core.Constants;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public partial class AccountActivityProjectionRebuilder(
        TradeContext context,
        ILogger<AccountActivityProjectionRebuilder> logger)
        : IAccountActivityProjectionRebuilder
    {
        public async Task<int> RebuildAsync(CancellationToken cancellationToken = default)
        {
            var transactions = await context.Transactions
                .AsNoTracking()
                .OrderBy(t => t.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var projections = new List<AccountActivityProjection>(transactions.Count * 2);
            var existingAccountIds = await context.Accounts
                .AsNoTracking()
                .Select(a => a.Id)
                .ToHashSetAsync(cancellationToken);

            foreach (var transaction in transactions)
            {
                DateTime? processedAtUtc = transaction.Status is TransactionStatus.Processed or TransactionStatus.Failed
                    ? transaction.CompletedAtUtc
                    : null;

                projections.Add(new AccountActivityProjection
                {
                    TransactionId = transaction.Id,
                    AccountId = transaction.SourceAccountId,
                    CounterpartyAccountId = transaction.TargetAccountId,
                    Direction = AccountActivityDirection.Outgoing,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency.Code,
                    Status = transaction.Status,
                    CreatedAtUtc = transaction.CreatedAtUtc,
                    ProcessedAtUtc = processedAtUtc,
                    LastEventUtc = processedAtUtc ?? transaction.CreatedAtUtc,
                    FailureReason = transaction.FailureReason
                });

                if (!existingAccountIds.Contains(transaction.TargetAccountId))
                {
                    continue;
                }

                projections.Add(new AccountActivityProjection
                {
                    TransactionId = transaction.Id,
                    AccountId = transaction.TargetAccountId,
                    CounterpartyAccountId = transaction.SourceAccountId,
                    Direction = AccountActivityDirection.Incoming,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency.Code,
                    Status = transaction.Status,
                    CreatedAtUtc = transaction.CreatedAtUtc,
                    ProcessedAtUtc = processedAtUtc,
                    LastEventUtc = processedAtUtc ?? transaction.CreatedAtUtc,
                    FailureReason = transaction.FailureReason
                });
            }

            await using var dbTransaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await context.AccountActivityProjections.ExecuteDeleteAsync(cancellationToken);
            await context.AccountActivityProjections.AddRangeAsync(projections, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            LogRebuilt(logger, projections.Count, transactions.Count);
            return projections.Count;
        }

        [LoggerMessage(LogLevel.Information, "Rebuilt account activity projection with {ProjectionCount} rows from {TransactionCount} transactions")]
        static partial void LogRebuilt(ILogger logger, int projectionCount, int transactionCount);
    }
}
