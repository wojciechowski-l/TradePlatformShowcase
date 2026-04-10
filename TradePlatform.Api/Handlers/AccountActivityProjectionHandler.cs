using Microsoft.EntityFrameworkCore;
using Rebus.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Api.Handlers;

public partial class AccountActivityProjectionHandler(
    IDbContextFactory<TradeContext> dbContextFactory,
    IMessageInbox messageInbox,
    IMessageMetadataAccessor messageMetadataAccessor,
    ILogger<AccountActivityProjectionHandler> logger)
    : IHandleMessages<TransactionSubmittedEvent>,
      IHandleMessages<TransactionStatusChangedEvent>
{
    public async Task Handle(TransactionSubmittedEvent message)
    {
        var messageId = messageMetadataAccessor.GetCurrentMessageId()
            ?? throw new InvalidOperationException("Missing Rebus message id for account activity projection.");
        await using var context = await dbContextFactory.CreateDbContextAsync();

        if (!await messageInbox.TryBeginProcessingAsync(
            context,
            $"{typeof(AccountActivityProjectionHandler).FullName}:{typeof(TransactionSubmittedEvent).FullName}",
            messageId))
        {
            LogDuplicateProjectionMessage(logger, message.TransactionId, messageId);
            return;
        }

        await UpsertProjectionAsync(
            context,
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

        if (await AccountExistsAsync(context, message.TargetAccountId))
        {
            await UpsertProjectionAsync(
                context,
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
        }

        await context.SaveChangesAsync();
        LogProjectionCreated(logger, message.TransactionId);
    }

    public async Task Handle(TransactionStatusChangedEvent message)
    {
        var messageId = messageMetadataAccessor.GetCurrentMessageId()
            ?? throw new InvalidOperationException("Missing Rebus message id for account activity projection.");
        await using var context = await dbContextFactory.CreateDbContextAsync();

        if (!await messageInbox.TryBeginProcessingAsync(
            context,
            $"{typeof(AccountActivityProjectionHandler).FullName}:{typeof(TransactionStatusChangedEvent).FullName}",
            messageId))
        {
            LogDuplicateProjectionMessage(logger, message.TransactionId, messageId);
            return;
        }

        var hasExistingProjection = await context.AccountActivityProjections
            .AnyAsync(p => p.TransactionId == message.TransactionId);

        if (!hasExistingProjection)
        {
            await UpsertProjectionAsync(
                context,
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

            if (await AccountExistsAsync(context, message.TargetAccountId))
            {
                await UpsertProjectionAsync(
                    context,
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
            }

            await context.SaveChangesAsync();
            LogProjectionRecovered(logger, message.TransactionId, message.CurrentStatus);
            return;
        }

        var changed = await BuildUpdatableProjectionQuery(context, message)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Status, message.CurrentStatus)
                .SetProperty(p => p.ProcessedAtUtc, GetCompletedAtUtc(message.CurrentStatus, message.ChangedAtUtc))
                .SetProperty(p => p.LastEventUtc, message.ChangedAtUtc)
                .SetProperty(p => p.FailureReason, message.FailureReason));

        if (changed == 0)
        {
            LogDuplicateStatusUpdate(logger, message.TransactionId, message.CurrentStatus);
            return;
        }

        LogProjectionUpdated(logger, message.TransactionId, message.CurrentStatus);
    }

    private async Task UpsertProjectionAsync(
        TradeContext context,
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

    private static IQueryable<AccountActivityProjection> BuildUpdatableProjectionQuery(
        TradeContext context,
        TransactionStatusChangedEvent message)
    {
        var query = context.AccountActivityProjections
            .Where(p => p.TransactionId == message.TransactionId);

        return message.CurrentStatus switch
        {
            TransactionStatus.Validated => query.Where(p =>
                p.Status == TransactionStatus.Pending ||
                (p.Status == TransactionStatus.Validated && p.LastEventUtc <= message.ChangedAtUtc)),

            TransactionStatus.Processing => query.Where(p =>
                p.Status == TransactionStatus.Pending ||
                p.Status == TransactionStatus.Validated ||
                (p.Status == TransactionStatus.Processing && p.LastEventUtc <= message.ChangedAtUtc)),

            TransactionStatus.Processed or TransactionStatus.Failed => query.Where(p =>
                p.Status == TransactionStatus.Pending ||
                p.Status == TransactionStatus.Validated ||
                p.Status == TransactionStatus.Processing),

            _ => query.Where(p =>
                p.Status == TransactionStatus.Pending && p.LastEventUtc <= message.ChangedAtUtc)
        };
    }

    private static Task<bool> AccountExistsAsync(TradeContext context, string accountId)
    {
        return context.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == accountId);
    }

    [LoggerMessage(LogLevel.Information, "Created activity projection entries for Tx {TransactionId}")]
    static partial void LogProjectionCreated(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Warning, "Activity projection missing for Tx {TransactionId} during status update")]
    static partial void LogProjectionMissing(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Information, "Updated activity projection for Tx {TransactionId} to {Status}")]
    static partial void LogProjectionUpdated(ILogger logger, Guid transactionId, TransactionStatus status);

    [LoggerMessage(LogLevel.Information, "Ignoring duplicate activity projection update for Tx {TransactionId} with status {Status}")]
    static partial void LogDuplicateStatusUpdate(ILogger logger, Guid transactionId, TransactionStatus status);

    [LoggerMessage(LogLevel.Information, "Skipping duplicate projection delivery for Tx {TransactionId} with message id {MessageId}")]
    static partial void LogDuplicateProjectionMessage(ILogger logger, Guid transactionId, string messageId);

    [LoggerMessage(LogLevel.Warning, "Recovered missing activity projection for Tx {TransactionId} from processed event with status {Status}")]
    static partial void LogProjectionRecovered(ILogger logger, Guid transactionId, TransactionStatus status);
}
