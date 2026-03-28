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
      IHandleMessages<TransactionStatusChangedEvent>
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
            message.SubmittedAtUtc,
            null,
            allowStatusRegression: false);

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
            message.SubmittedAtUtc,
            null,
            allowStatusRegression: false);

        await context.SaveChangesAsync();
        LogProjectionCreated(logger, message.TransactionId);
    }

    public async Task Handle(TransactionStatusChangedEvent message)
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
                message.CurrentStatus,
                message.ChangedAtUtc,
                GetCompletedAtUtc(message.CurrentStatus, message.ChangedAtUtc),
                message.ChangedAtUtc,
                message.FailureReason,
                allowStatusRegression: true);

            await UpsertProjectionAsync(
                message.TransactionId,
                message.TargetAccountId,
                message.SourceAccountId,
                AccountActivityDirection.Incoming,
                message.Amount,
                message.Currency,
                message.CurrentStatus,
                message.ChangedAtUtc,
                GetCompletedAtUtc(message.CurrentStatus, message.ChangedAtUtc),
                message.ChangedAtUtc,
                message.FailureReason,
                allowStatusRegression: true);

            await context.SaveChangesAsync();
            LogProjectionRecovered(logger, message.TransactionId, message.CurrentStatus);
            return;
        }

        if (projections.All(p =>
            p.Status == message.CurrentStatus &&
            (p.Status is TransactionStatus.Processed or TransactionStatus.Failed
                ? p.ProcessedAtUtc.HasValue
                : p.LastEventUtc >= message.ChangedAtUtc)))
        {
            LogDuplicateStatusUpdate(logger, message.TransactionId, message.CurrentStatus);
            return;
        }

        foreach (var projection in projections)
        {
            if (GetStatusOrder(message.CurrentStatus) < GetStatusOrder(projection.Status))
            {
                continue;
            }

            if (projection.Status == message.CurrentStatus &&
                projection.Status is TransactionStatus.Processed or TransactionStatus.Failed &&
                projection.ProcessedAtUtc.HasValue)
            {
                continue;
            }

            if (projection.LastEventUtc > message.ChangedAtUtc &&
                GetStatusOrder(message.CurrentStatus) == GetStatusOrder(projection.Status))
            {
                continue;
            }

            projection.Status = message.CurrentStatus;
            projection.ProcessedAtUtc = GetCompletedAtUtc(message.CurrentStatus, message.ChangedAtUtc);
            projection.LastEventUtc = message.ChangedAtUtc;
            projection.FailureReason = message.FailureReason;
        }

        await context.SaveChangesAsync();
        LogProjectionUpdated(logger, message.TransactionId, message.CurrentStatus);
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
        DateTime lastEventUtc,
        string? failureReason,
        bool allowStatusRegression)
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
                LastEventUtc = lastEventUtc,
                FailureReason = failureReason
            });

            return;
        }

        projection.CounterpartyAccountId = counterpartyAccountId;
        projection.Amount = amount;
        projection.Currency = currency;
        projection.CreatedAtUtc = createdAtUtc < projection.CreatedAtUtc ? createdAtUtc : projection.CreatedAtUtc;

        if (allowStatusRegression ||
            GetStatusOrder(status) > GetStatusOrder(projection.Status) ||
            (GetStatusOrder(status) == GetStatusOrder(projection.Status) && lastEventUtc >= projection.LastEventUtc))
        {
            projection.Status = status;
            projection.ProcessedAtUtc = processedAtUtc;
            projection.LastEventUtc = lastEventUtc;
            projection.FailureReason = failureReason;
        }
    }

    private static int GetStatusOrder(TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Pending => 0,
            TransactionStatus.Validated => 1,
            TransactionStatus.Processing => 2,
            TransactionStatus.Processed => 3,
            TransactionStatus.Failed => 3,
            _ => 0
        };
    }

    private static DateTime? GetCompletedAtUtc(TransactionStatus status, DateTime changedAtUtc)
    {
        return status is TransactionStatus.Processed or TransactionStatus.Failed
            ? changedAtUtc
            : null;
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
