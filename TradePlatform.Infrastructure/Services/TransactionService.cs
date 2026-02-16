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
    public partial class TransactionService(ITradeContext context, IBus bus, ILogger<TransactionService> logger) : ITransactionService
    {
        private readonly ITradeContext _context = context;
        private readonly IBus _bus = bus;
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
            using var scope = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

            var transaction = new TransactionRecord
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
                transaction.Id,
                transaction.SourceAccountId,
                transaction.TargetAccountId,
                transaction.Amount,
                transaction.Currency.Code
            );

            _context.Transactions.Add(transaction);

            await _context.SaveChangesAsync();

            await _bus.Send(eventPayload);

            scope.Complete();

            var tags = new KeyValuePair<string, object?>[]
            {
                new("currency", request.Currency)
            };

            TradesCreatedCounter.Add(1, tags);
            TradeAmountHistogram.Record((double)request.Amount, tags);

            LogTransactionCreated(transaction.Id, transaction.Amount, transaction.Currency.Code);

            return new CreateTransactionResult
            {
                TransactionId = transaction.Id,
                Status = TransactionStatus.Pending
            };
        }
    }
}