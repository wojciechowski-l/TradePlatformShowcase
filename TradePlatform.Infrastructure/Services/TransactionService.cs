using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Infrastructure.Services
{
    public partial class TransactionService(
    ITradeContext context,
    IBus bus,
    ITransactionScopeManager transactionScopeManager,
    ILogger<TransactionService> logger) : ITransactionService
    {
        private readonly ITradeContext _context = context;
        private readonly IBus _bus = bus;
        private readonly ITransactionScopeManager _transactionScopeManager = transactionScopeManager;
        private readonly ILogger<TransactionService> _logger = logger;

        private static readonly Meter Meter = new("TradePlatform.Transactions", "1.0.0");
        private static readonly Counter<long> TradesCreatedCounter = Meter.CreateCounter<long>("trades_created_total", description: "Total number of trades created");
        private static readonly Histogram<double> TradeAmountHistogram = Meter.CreateHistogram<double>("trade_amount", unit: "currency", description: "Distribution of trade amounts");

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Created transaction {TransactionId} for {Amount} {Currency}")]
        private partial void LogTransactionCreated(Guid transactionId, decimal amount, string currency);

        public async Task<CreateTransactionResult> CreateTransactionAsync(TransactionDto request)
        {
            return await _transactionScopeManager.ExecuteInTransactionAsync(async () =>
            {
                var transactionRecord = new TransactionRecord
                {
                    Id = Guid.NewGuid(),
                    SourceAccountId = request.SourceAccountId,
                    TargetAccountId = request.TargetAccountId,
                    Amount = request.Amount,
                    Currency = Currency.FromCode(request.Currency),
                    Status = TransactionStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var eventPayload = new TransactionCreatedEvent(
                    transactionRecord.Id,
                    transactionRecord.SourceAccountId,
                    transactionRecord.TargetAccountId,
                    transactionRecord.Amount,
                    transactionRecord.Currency.Code
                );

                _context.Transactions.Add(transactionRecord);

                await _context.SaveChangesAsync();

                await _bus.Send(eventPayload);

                var tags = new KeyValuePair<string, object?>[]
                {
                new("currency", request.Currency)
                };

                TradesCreatedCounter.Add(1, tags);
                TradeAmountHistogram.Record((double)request.Amount, tags);

                LogTransactionCreated(transactionRecord.Id, transactionRecord.Amount, transactionRecord.Currency.Code);

                return new CreateTransactionResult
                {
                    TransactionId = transactionRecord.Id,
                    Status = TransactionStatus.Pending
                };
            });
        }
    }
}