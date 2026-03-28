using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Rebus.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Worker.Handlers;

public partial class TransactionCreatedHandler(
TradeContext dbContext,
IBus bus,
ITransactionScopeManager transactionScopeManager,
ILogger<TransactionCreatedHandler> logger)
: IHandleMessages<TransactionCreatedEvent>
{
    public async Task Handle(TransactionCreatedEvent evt)
    {
        LogProcessing(logger, evt.TransactionId);

        TransactionStatus? terminalStatus = null;

        await transactionScopeManager.ExecuteInTransactionAsync(async () =>
        {
            var transaction = await dbContext.Transactions
                .FirstOrDefaultAsync(t => t.Id == evt.TransactionId);

            if (transaction == null)
            {
                LogNotFound(logger, evt.TransactionId);
                return;
            }

            if (transaction.Status is TransactionStatus.Processed or TransactionStatus.Failed)
            {
                LogAlreadyCompleted(logger, evt.TransactionId, transaction.Status);
                return;
            }

            var sourceAccount = await dbContext.Accounts
                .FirstOrDefaultAsync(a => a.Id == transaction.SourceAccountId);

            var targetAccount = await dbContext.Accounts
                .FirstOrDefaultAsync(a => a.Id == transaction.TargetAccountId);

            var validationFailure = ValidateTransaction(transaction, sourceAccount, targetAccount);

            if (validationFailure is not null)
            {
                var failedAtUtc = DateTime.UtcNow;
                var failedFromStatus = transaction.MarkFailed(failedAtUtc, validationFailure);

                await dbContext.SaveChangesAsync();
                await PublishStatusChangedAsync(transaction, failedFromStatus, failedAtUtc, validationFailure);

                terminalStatus = TransactionStatus.Failed;
                return;
            }

            var validatedAtUtc = DateTime.UtcNow;
            var previousStatus = transaction.MarkValidated(validatedAtUtc);

            await dbContext.SaveChangesAsync();
            await PublishStatusChangedAsync(transaction, previousStatus, validatedAtUtc);

            var processingStartedAtUtc = DateTime.UtcNow;
            previousStatus = transaction.MarkProcessing(processingStartedAtUtc);

            await dbContext.SaveChangesAsync();
            await PublishStatusChangedAsync(transaction, previousStatus, processingStartedAtUtc);

            sourceAccount!.Balance -= transaction.Amount;
            targetAccount!.Balance += transaction.Amount;

            var processedAtUtc = DateTime.UtcNow;
            previousStatus = transaction.MarkProcessed(processedAtUtc);

            await dbContext.SaveChangesAsync();
            await PublishStatusChangedAsync(transaction, previousStatus, processedAtUtc);

            terminalStatus = TransactionStatus.Processed;
        });

        if (terminalStatus == TransactionStatus.Processed)
        {
            LogSuccess(logger, evt.TransactionId);
        }
        else if (terminalStatus == TransactionStatus.Failed)
        {
            LogFailed(logger, evt.TransactionId);
        }
    }

    private async Task PublishStatusChangedAsync(
        Core.Entities.TransactionRecord transaction,
        TransactionStatus previousStatus,
        DateTime changedAtUtc,
        string? failureReason = null)
    {
        await bus.Publish(new TransactionStatusChangedEvent(
            transaction.Id,
            transaction.SourceAccountId,
            transaction.TargetAccountId,
            transaction.Amount,
            transaction.Currency.Code,
            previousStatus,
            transaction.Status,
            changedAtUtc,
            failureReason));
    }

    private static string? ValidateTransaction(
        Core.Entities.TransactionRecord transaction,
        Core.Entities.Account? sourceAccount,
        Core.Entities.Account? targetAccount)
    {
        if (sourceAccount is null)
        {
            return "Source account does not exist.";
        }

        if (targetAccount is null)
        {
            return "Target account does not exist.";
        }

        if (sourceAccount.Id == targetAccount.Id)
        {
            return "Source and target accounts must be different.";
        }

        if (sourceAccount.Currency.Code != transaction.Currency.Code)
        {
            return "Source account currency does not match the transaction currency.";
        }

        if (targetAccount.Currency.Code != transaction.Currency.Code)
        {
            return "Target account currency does not match the transaction currency.";
        }

        if (sourceAccount.Balance < transaction.Amount)
        {
            return "Source account has insufficient funds.";
        }

        return null;
    }

    [LoggerMessage(LogLevel.Information, "Processing Transaction {TransactionId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Warning, "Transaction {TransactionId} not found.")]
    static partial void LogNotFound(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Information, "Transaction {TransactionId} already completed with status {Status}.")]
    static partial void LogAlreadyCompleted(ILogger logger, Guid transactionId, TransactionStatus status);

    [LoggerMessage(LogLevel.Information, "Transaction {TransactionId} processed successfully.")]
    static partial void LogSuccess(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Warning, "Transaction {TransactionId} failed validation or processing.")]
    static partial void LogFailed(ILogger logger, Guid transactionId);
}
