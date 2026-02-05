using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Messaging
{
    public partial class OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPublisherWorker> logger,
        IPublishEndpoint publishEndpoint,
        IConfiguration configuration
    ) : BackgroundService
    {
        private readonly ILogger<OutboxPublisherWorker> _logger = logger;
        private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;

        private const int MaxAttempts = 5;
        private const int BatchSize = 50;

        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(
            int.TryParse(configuration["Outbox:PollIntervalSeconds"], out var seconds) ? seconds : 2);

        private readonly TimeSpan _stuckThreshold = TimeSpan.FromMinutes(
            int.TryParse(configuration["Outbox:StuckThresholdMinutes"], out var minutes) ? minutes : 5);

        private static readonly string ReservationQuery = $"""
            WITH CTE AS (
                SELECT TOP (@batchSize) *
                FROM OutboxMessages WITH (UPDLOCK, READPAST)
                WHERE Status = 0 -- Pending
            )
            UPDATE CTE
            SET Status = 1, -- InFlight
                LastAttemptAtUtc = GETUTCDATE(),
                AttemptCount = AttemptCount + 1
            OUTPUT inserted.*;
            """;

        private static readonly string SweeperQuery = $"""
            UPDATE OutboxMessages
            SET Status = 0, -- Reset to Pending
                LastAttemptAtUtc = NULL,
                LastError = 'Rescued by sweeper (Stuck)'
            WHERE Status = 1 -- InFlight
            AND LastAttemptAtUtc < @cutoff;
            """;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TradeContext>();

                    var sweeperCutoff = DateTime.UtcNow.Subtract(_stuckThreshold);
                    var rescuedCount = await db.Database.ExecuteSqlRawAsync(
                        SweeperQuery,
                        [new Microsoft.Data.SqlClient.SqlParameter("@cutoff", sweeperCutoff)],
                        stoppingToken);

                    if (rescuedCount > 0) LogSweeperRescued(rescuedCount);

                    var messages = await db.OutboxMessages
                        .FromSqlRaw(ReservationQuery, new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize))
                        .ToListAsync(stoppingToken);

                    foreach (var message in messages)
                    {
                        try
                        {
                            var eventPayload = JsonSerializer.Deserialize<TransactionCreatedEvent>(message.Payload);

                            if (eventPayload != null)
                            {
                                await _publishEndpoint.Publish(eventPayload, context => {
                                    context.MessageId = message.Id;
                                }, stoppingToken);
                            }

                            message.Status = OutboxStatus.Processed;
                            message.ProcessedAtUtc = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            message.AttemptCount++;
                            message.LastError = ex.Message;
                            if (message.AttemptCount >= MaxAttempts)
                            {
                                message.Status = OutboxStatus.Failed;
                                LogDeadLettered(ex, message.Id, MaxAttempts);
                            }
                            else
                            {
                                message.Status = OutboxStatus.Pending;
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