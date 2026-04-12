using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradePlatform.Infrastructure.Configuration;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services;

public class MessagingRetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<MessagingReliabilityOptions> options,
    ILogger<MessagingRetentionService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly MessagingReliabilityOptions _options = options.Value;
    private readonly ILogger<MessagingRetentionService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RetentionSweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TradeContext>();

            var deletedInbox = await DeleteExpiredRowsAsync(
                context,
                "[dbo].[InboxMessages]",
                "[ProcessedAtUtc]",
                DateTime.UtcNow.Subtract(_options.InboxRetention),
                cancellationToken);

            var deletedIdempotency = await DeleteExpiredRowsAsync(
                context,
                "[dbo].[IdempotencyKeys]",
                "[CreatedAtUtc]",
                DateTime.UtcNow.Subtract(_options.IdempotencyRetention),
                cancellationToken);

            var deletedOutbox = await DeleteExpiredRowsAsync(
                context,
                "[dbo].[RebusOutbox]",
                "[creation_time]",
                DateTime.UtcNow.Subtract(_options.OutboxRetention),
                cancellationToken);

            MessagingMetrics.RecordRetentionDelete("InboxMessages", deletedInbox);
            MessagingMetrics.RecordRetentionDelete("IdempotencyKeys", deletedIdempotency);
            MessagingMetrics.RecordRetentionDelete("RebusOutbox", deletedOutbox);

            if (deletedInbox + deletedIdempotency + deletedOutbox > 0)
            {
                _logger.LogInformation(
                    "Messaging retention deleted {InboxRows} inbox rows, {IdempotencyRows} idempotency rows, and {OutboxRows} outbox rows.",
                    deletedInbox,
                    deletedIdempotency,
                    deletedOutbox);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Messaging retention sweep failed.");
        }
    }

    private async Task<long> DeleteExpiredRowsAsync(
        TradeContext context,
        string tableName,
        string cutoffColumn,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0L;

        for (var batchNumber = 0; batchNumber < _options.MaxBatchesPerSweep; batchNumber++)
        {
            var deletedRows = await ExecuteDeleteBatchAsync(context, tableName, cutoffColumn, cutoffUtc, cancellationToken);
            totalDeleted += deletedRows;

            if (deletedRows < _options.RetentionBatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    private async Task<long> ExecuteDeleteBatchAsync(
        TradeContext context,
        string tableName,
        string cutoffColumn,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                SELECT CAST(0 AS BIGINT);
            ELSE
            BEGIN
                DELETE TOP (@batchSize) FROM {tableName}
                WHERE {cutoffColumn} < @cutoffUtc;

                SELECT CAST(@@ROWCOUNT AS BIGINT);
            END
            """;

        var batchSize = command.CreateParameter();
        batchSize.ParameterName = "@batchSize";
        batchSize.Value = _options.RetentionBatchSize;
        command.Parameters.Add(batchSize);

        var cutoff = command.CreateParameter();
        cutoff.ParameterName = "@cutoffUtc";
        cutoff.Value = cutoffUtc;
        command.Parameters.Add(cutoff);

        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await command.Connection!.OpenAsync(cancellationToken);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long value ? value : Convert.ToInt64(result);
    }
}
