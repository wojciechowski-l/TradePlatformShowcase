using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using System.Diagnostics.Metrics;
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

        public async Task<CreateTransactionResult> CreateTransactionAsync(
            TransactionDto request,
            string? idempotencyKey,
            string userId,
            CancellationToken cancellationToken = default)
        {
            return await _transactionScopeManager.ExecuteInTransactionAsync(async () =>
            {
                if (idempotencyKey is not null)
                {
                    var existingResult = await GetExistingTransactionAsync(idempotencyKey, userId, cancellationToken);

                    if (existingResult is not null)
                    {
                        MessagingMetrics.RecordRequestIdempotencyHit();
                        return existingResult;
                    }
                }

                var transactionRecord = new TransactionRecord
                {
                    SourceAccountId = request.SourceAccountId,
                    TargetAccountId = request.TargetAccountId,
                    Amount = request.Amount,
                    Currency = Currency.FromCode(request.Currency),
                    Status = TransactionStatus.Pending,
                };

                var eventPayload = new TransactionCreatedEvent(
                    transactionRecord.Id,
                    transactionRecord.SourceAccountId,
                    transactionRecord.TargetAccountId,
                    transactionRecord.Amount,
                    transactionRecord.Currency.Code
                );

                _context.Transactions.Add(transactionRecord);

                if (idempotencyKey is not null)
                {
                    _context.IdempotencyKeys.Add(new IdempotencyKey
                    {
                        Key = idempotencyKey,
                        UserId = userId,
                        TransactionId = transactionRecord.Id
                    });
                }

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (idempotencyKey is not null && IsUniqueConstraintViolation(ex))
                {
                    if (_context is DbContext dbContext)
                    {
                        dbContext.ChangeTracker.Clear();
                    }

                    var existingResult = await GetExistingTransactionAsync(idempotencyKey, userId, cancellationToken);
                    if (existingResult is not null)
                    {
                        MessagingMetrics.RecordRequestIdempotencyHit();
                        return existingResult;
                    }

                    throw;
                }

                await _bus.Send(eventPayload);
                await _bus.Publish(new TransactionSubmittedEvent(
                    transactionRecord.Id,
                    transactionRecord.SourceAccountId,
                    transactionRecord.TargetAccountId,
                    transactionRecord.Amount,
                    transactionRecord.Currency.Code,
                    transactionRecord.CreatedAtUtc));

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
            }, cancellationToken);
        }

        public async Task<CreateTransactionResult?> GetExistingTransactionAsync(
            string idempotencyKey,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var ttlCutoff = DateTime.UtcNow.AddHours(-24);
            var existing = await _context.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    k => k.Key == idempotencyKey
                      && k.UserId == userId
                      && k.CreatedAtUtc > ttlCutoff,
                    cancellationToken);

            if (existing is null)
            {
                return null;
            }

            var status = await _context.Transactions
                .AsNoTracking()
                .Where(t => t.Id == existing.TransactionId)
                .Select(t => (TransactionStatus?)t.Status)
                .FirstOrDefaultAsync(cancellationToken);

            return new CreateTransactionResult
            {
                TransactionId = existing.TransactionId,
                Status = status ?? TransactionStatus.Pending
            };
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }
    }
}
