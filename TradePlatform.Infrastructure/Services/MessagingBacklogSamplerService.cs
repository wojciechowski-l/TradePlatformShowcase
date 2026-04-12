using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TradePlatform.Core.Constants;
using TradePlatform.Infrastructure.Configuration;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services;

public class MessagingBacklogSamplerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<MessagingReliabilityOptions> options,
    ILogger<MessagingBacklogSamplerService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly MessagingReliabilityOptions _options = options.Value;
    private readonly ILogger<MessagingBacklogSamplerService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SamplingInterval);

        await SampleAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SampleAsync(stoppingToken);
        }
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TradeContext>();

            var inboxRows = await context.InboxMessages.LongCountAsync(cancellationToken);
            var idempotencyRows = await context.IdempotencyKeys.LongCountAsync(cancellationToken);
            var outboxRows = await CountOptionalTableRowsAsync(context, "[dbo].[RebusOutbox]", cancellationToken);

            MessagingMetrics.UpdateStorageRows(inboxRows, idempotencyRows, outboxRows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sample messaging table sizes.");
        }

        try
        {
            var rabbitConnectionString = RabbitMqConnectionStringFactory.Create(_configuration);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitConnectionString)
            };

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            var ordersDeadLetter = await GetQueueDepthAsync(channel, MessagingConstants.OrdersDeadLetterQueue, cancellationToken);
            var notificationsDeadLetter = await GetQueueDepthAsync(channel, MessagingConstants.NotificationsDeadLetterQueue, cancellationToken);

            MessagingMetrics.UpdateDeadLetterRows(ordersDeadLetter, notificationsDeadLetter);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sample dead-letter queue backlog.");
        }
    }

    private static async Task<long> CountOptionalTableRowsAsync(
        TradeContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                SELECT CAST(0 AS BIGINT);
            ELSE
                SELECT COUNT_BIG(*) FROM {tableName};
            """;

        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await command.Connection!.OpenAsync(cancellationToken);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long value ? value : Convert.ToInt64(result);
    }

    private static async Task<long> GetQueueDepthAsync(
        IChannel channel,
        string queueName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);
            return (long)response.MessageCount;
        }
        catch
        {
            return 0;
        }
    }
}
