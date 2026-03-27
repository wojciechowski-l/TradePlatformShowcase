using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Api.Handlers;

public partial class AccountActivityProjectionHandler(
    TradeContext context,
    ILogger<AccountActivityProjectionHandler> logger)
    : IHandleMessages<TransactionSubmittedEvent>,
      IHandleMessages<TransactionProcessedEvent>
{
    public async Task Handle(TransactionSubmittedEvent message)
    {
        await UpsertProjectionAsync(
            message.TransactionId,
            message.SourceAccountId,
            message.TargetAccountId,
            AccountActivityDirection.Outgoing,
            message.Amount,
            message.Currency,
            TransactionStatus.Pending,
            message.SubmittedAtUtc,
            null,
            message.SubmittedAtUtc);

        await UpsertProjectionAsync(
            message.TransactionId,
            message.TargetAccountId,
            message.SourceAccountId,
            AccountActivityDirection.Incoming,
            message.Amount,
            message.Currency,
            TransactionStatus.Pending,
            message.SubmittedAtUtc,
            null,
            message.SubmittedAtUtc);

        await context.SaveChangesAsync();
        LogProjectionCreated(logger, message.TransactionId);
    }

    public async Task Handle(TransactionProcessedEvent message)
    {
        var projections = await context.AccountActivityProjections
            .Where(p => p.TransactionId == message.TransactionId)
            .ToListAsync();

        if (projections.Count == 0)
        {
            await UpsertProjectionAsync(
                message.TransactionId,
                message.SourceAccountId,
                message.TargetAccountId,
                AccountActivityDirection.Outgoing,
                message.Amount,
                message.Currency,
                message.Status,
                message.ProcessedAtUtc,
                message.ProcessedAtUtc,
                message.ProcessedAtUtc);

            await UpsertProjectionAsync(
                message.TransactionId,
                message.TargetAccountId,
                message.SourceAccountId,
                AccountActivityDirection.Incoming,
                message.Amount,
                message.Currency,
                message.Status,
                message.ProcessedAtUtc,
                message.ProcessedAtUtc,
                message.ProcessedAtUtc);

            await context.SaveChangesAsync();
            LogProjectionRecovered(logger, message.TransactionId, message.Status);
            return;
        }

        if (projections.All(p => p.Status == message.Status && p.ProcessedAtUtc.HasValue))
        {
            LogDuplicateStatusUpdate(logger, message.TransactionId, message.Status);
            return;
        }

        foreach (var projection in projections)
        {
            projection.Status = message.Status;
            projection.ProcessedAtUtc = message.ProcessedAtUtc;
            projection.LastEventUtc = message.ProcessedAtUtc;
        }

        await context.SaveChangesAsync();
        LogProjectionUpdated(logger, message.TransactionId, message.Status);
    }

    private async Task UpsertProjectionAsync(
        Guid transactionId,
        string accountId,
        string counterpartyAccountId,
        AccountActivityDirection direction,
        decimal amount,
        string currency,
        TransactionStatus status,
        DateTime createdAtUtc,
        DateTime? processedAtUtc,
        DateTime lastEventUtc)
    {
        var projection = await context.AccountActivityProjections
            .FirstOrDefaultAsync(p =>
                p.TransactionId == transactionId &&
                p.AccountId == accountId &&
                p.Direction == direction);

        if (projection is null)
        {
            context.AccountActivityProjections.Add(new AccountActivityProjection
            {
                TransactionId = transactionId,
                AccountId = accountId,
                CounterpartyAccountId = counterpartyAccountId,
                Direction = direction,
                Amount = amount,
                Currency = currency,
                Status = status,
                CreatedAtUtc = createdAtUtc,
                ProcessedAtUtc = processedAtUtc,
                LastEventUtc = lastEventUtc
            });

            return;
        }

        projection.CounterpartyAccountId = counterpartyAccountId;
        projection.Amount = amount;
        projection.Currency = currency;
        projection.Status = status;
        projection.CreatedAtUtc = createdAtUtc;
        projection.ProcessedAtUtc = processedAtUtc;
        projection.LastEventUtc = lastEventUtc;
    }

    [LoggerMessage(LogLevel.Information, "Created activity projection entries for Tx {TransactionId}")]
    static partial void LogProjectionCreated(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Warning, "Activity projection missing for Tx {TransactionId} during status update")]
    static partial void LogProjectionMissing(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Information, "Updated activity projection for Tx {TransactionId} to {Status}")]
    static partial void LogProjectionUpdated(ILogger logger, Guid transactionId, TransactionStatus status);

    [LoggerMessage(LogLevel.Information, "Ignoring duplicate activity projection update for Tx {TransactionId} with status {Status}")]
    static partial void LogDuplicateStatusUpdate(ILogger logger, Guid transactionId, TransactionStatus status);

    [LoggerMessage(LogLevel.Warning, "Recovered missing activity projection for Tx {TransactionId} from processed event with status {Status}")]
    static partial void LogProjectionRecovered(ILogger logger, Guid transactionId, TransactionStatus status);
}
