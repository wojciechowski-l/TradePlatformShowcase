using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Messaging
{
    public partial class OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherWorker> logger,
        IConfiguration configuration
    ) : BackgroundService
    {
        private const int MaxAttempts = 5;
        private const int BatchSize = 50;
        private const int LeaseOffsetYears = 100;
        private static readonly DateTime SafetyCutoffDateUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(
            int.TryParse(configuration["Outbox:PollIntervalSeconds"], out var seconds) ? seconds : 2);

        private readonly TimeSpan _stuckThreshold = TimeSpan.FromMinutes(
            int.TryParse(configuration["Outbox:StuckThresholdMinutes"], out var minutes) ? minutes : 5);

        private static readonly string ReservationQuery = $"""
            WITH CTE AS (
                SELECT TOP (@batchSize) *
                FROM OutboxMessages WITH (UPDLOCK, READPAST)
                WHERE ProcessedAtUtc IS NULL
                  AND AttemptCount < @maxAttempts
                ORDER BY CreatedAtUtc
            )
            UPDATE CTE
            SET ProcessedAtUtc = DATEADD(year, -{LeaseOffsetYears}, GETUTCDATE())
            OUTPUT inserted.Id, inserted.Type, inserted.Payload,
                   inserted.CreatedAtUtc, inserted.ProcessedAtUtc,
                   inserted.AttemptCount, inserted.LastError;
            """;

        private static readonly string SweeperQuery = $"""
            UPDATE OutboxMessages
            SET ProcessedAtUtc = NULL,
                AttemptCount = AttemptCount + 1,
                LastError = CONCAT(
                    'Rescued by sweeper. Was stuck for ',
                    DATEDIFF(second, DATEADD(year, {LeaseOffsetYears}, ProcessedAtUtc), GETUTCDATE()),
                    ' seconds.'
                )
            WHERE ProcessedAtUtc IS NOT NULL
              AND ProcessedAtUtc < @cutoff
              AND ProcessedAtUtc < '{SafetyCutoffDateUtc:yyyy-MM-dd}';
            """;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TradeContext>();
                    var producer = scope.ServiceProvider.GetRequiredService<IMessageProducer>();

                    var lockTimeOrigin = DateTime.UtcNow.AddYears(-LeaseOffsetYears);
                    var sweeperCutoff = lockTimeOrigin.Subtract(_stuckThreshold);

                    var rescuedCount = await db.Database.ExecuteSqlRawAsync(
                        SweeperQuery,
                        [new Microsoft.Data.SqlClient.SqlParameter("@cutoff", sweeperCutoff)],
                        cancellationToken: stoppingToken);

                    if (rescuedCount > 0) LogSweeperRescued(rescuedCount);

                    var messages = await db.OutboxMessages
                        .FromSqlRaw(ReservationQuery,
                            new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize),
                            new Microsoft.Data.SqlClient.SqlParameter("@maxAttempts", MaxAttempts))
                        .ToListAsync(stoppingToken);

                    foreach (var message in messages)
                    {
                        try
                        {
                            var id = JsonSerializer.Deserialize<Guid>(message.Payload);
                            await producer.SendMessageAsync(id);
                            message.ProcessedAtUtc = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            message.AttemptCount++;
                            message.LastError = ex.Message;
                            if (message.AttemptCount >= MaxAttempts)
                            {
                                message.ProcessedAtUtc = DateTime.UtcNow;
                                LogDeadLettered(ex, message.Id, MaxAttempts);
                            }
                            else
                            {
                                message.ProcessedAtUtc = null;
                                LogPublishFailed(ex, message.Id, message.AttemptCount, MaxAttempts);
                            }
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    LogCycleFailed(ex);
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        [LoggerMessage(LogLevel.Error, "OutboxMessage {Id} exceeded max attempts ({Max}). Dead-lettered.")]
        private partial void LogDeadLettered(Exception ex, Guid id, int max);

        [LoggerMessage(LogLevel.Warning, "Failed to publish OutboxMessage {Id}. Attempt {Attempt}/{Max}.")]
        private partial void LogPublishFailed(Exception ex, Guid id, int attempt, int max);

        [LoggerMessage(LogLevel.Warning, "Sweeper rescued {Count} OutboxMessage(s) stuck at InFlight.")]
        private partial void LogSweeperRescued(int count);

        [LoggerMessage(LogLevel.Error, "OutboxPublisherWorker cycle failed.")]
        private partial void LogCycleFailed(Exception ex);
    }
}