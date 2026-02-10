using Microsoft.EntityFrameworkCore;
using Rebus.Bus;
using Rebus.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Worker.Handlers;

public partial class TransactionCreatedHandler(TradeContext dbContext, IBus bus, ILogger<TransactionCreatedHandler> logger)
    : IHandleMessages<TransactionCreatedEvent>
{
    public async Task Handle(TransactionCreatedEvent evt)
    {
        LogProcessing(logger, evt.TransactionId);

        var transaction = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == evt.TransactionId);

        if (transaction == null)
        {
            LogNotFound(logger, evt.TransactionId);
            return;
        }

        if (transaction.Status == TransactionStatus.Processed)
        {
            LogAlreadyProcessed(logger, evt.TransactionId);
            return;
        }

        using var scope = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        transaction.Status = TransactionStatus.Processed;

        await dbContext.SaveChangesAsync();

        var update = new TransactionUpdateDto
        {
            TransactionId = evt.TransactionId,
            Status = TransactionStatus.Processed,
            AccountId = evt.SourceAccountId,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await bus.Send(update);

        scope.Complete();

        LogSuccess(logger, evt.TransactionId);
    }

    [LoggerMessage(LogLevel.Information, "Processing Transaction {TransactionId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Warning, "Transaction {TransactionId} not found.")]
    static partial void LogNotFound(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Information, "Transaction {TransactionId} already processed.")]
    static partial void LogAlreadyProcessed(ILogger logger, Guid transactionId);

    [LoggerMessage(LogLevel.Information, "Transaction {TransactionId} processed successfully.")]
    static partial void LogSuccess(ILogger logger, Guid transactionId);
}