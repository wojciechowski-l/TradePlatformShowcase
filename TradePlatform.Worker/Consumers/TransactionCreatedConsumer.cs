using MassTransit;
using Microsoft.EntityFrameworkCore;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Worker.Consumers
{
    public partial class TransactionCreatedConsumer(
        TradeContext dbContext,
        ILogger<TransactionCreatedConsumer> logger
    ) : IConsumer<TransactionCreatedEvent>
    {
        private readonly ILogger<TransactionCreatedConsumer> _logger = logger;

        public async Task Consume(ConsumeContext<TransactionCreatedEvent> context)
        {
            var evt = context.Message;
            LogProcessingTransaction(evt.TransactionId);

            var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Transactions SET Status = {TransactionStatus.Processed} WHERE Id = {evt.TransactionId} AND Status = {TransactionStatus.Pending}");

            if (rowsAffected == 1)
            {
                LogTransactionProcessed(evt.TransactionId);

                var update = new TransactionUpdateDto
                {
                    TransactionId = evt.TransactionId,
                    Status = TransactionStatus.Processed,
                    AccountId = evt.SourceAccountId,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await context.Publish(update);
            }
            else
            {
                var exists = await dbContext.Transactions.AnyAsync(t => t.Id == evt.TransactionId);
                if (exists)
                {
                    LogTransactionAlreadyProcessed(evt.TransactionId);
                }
                else
                {
                    LogTransactionNotFound(evt.TransactionId);
                }
            }
        }

        [LoggerMessage(LogLevel.Information, "Processing Transaction {TransactionId}")]
        private partial void LogProcessingTransaction(Guid transactionId);

        [LoggerMessage(LogLevel.Information, "Transaction {TransactionId} processed successfully.")]
        private partial void LogTransactionProcessed(Guid transactionId);

        [LoggerMessage(LogLevel.Warning, "Transaction {TransactionId} was already processed.")]
        private partial void LogTransactionAlreadyProcessed(Guid transactionId);

        [LoggerMessage(LogLevel.Warning, "Transaction {TransactionId} not found in DB.")]
        private partial void LogTransactionNotFound(Guid transactionId);
    }
}