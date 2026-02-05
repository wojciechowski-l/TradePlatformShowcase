using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;

namespace TradePlatform.Worker;

public partial class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory scopeFactory,
    IRabbitMQConnection mqConnection
) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IRabbitMQConnection _mqConnection = mqConnection;

    private IChannel? _channel;

    private const int MaxRetryCount = 5;
    private const string RetryHeader = MessagingConstants.RetryHeader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_channel != null)
                {
                    try { await _channel.DisposeAsync(); }
                    catch { }
                    _channel = null;
                }

                LogConnecting();

                _channel = await _mqConnection.CreateChannelAsync();

                var channelClosedTcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                _channel.ChannelShutdownAsync += (_, args) =>
                {
                    LogChannelShutdown(args.ReplyText);
                    channelClosedTcs.TrySetResult(true);
                    return Task.CompletedTask;
                };

                await _channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: 1,
                    global: false,
                    cancellationToken: stoppingToken);

                LogConnected();

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    Guid transactionId = Guid.Empty;

                    try
                    {
                        var message = Encoding.UTF8.GetString(ea.Body.ToArray());

                        var eventPayload = JsonSerializer.Deserialize<TransactionCreatedEvent>(message);

                        if (eventPayload == null || eventPayload.TransactionId == Guid.Empty)
                        {
                            LogEmptyGuid();
                            await AckAsync(ea);
                            return;
                        }

                        transactionId = eventPayload.TransactionId;
                        LogProcessingMsg(transactionId);

                        await ProcessTransactionAsync(eventPayload);

                        await AckAsync(ea);
                    }
                    catch (JsonException jsonEx)
                    {
                        LogPoisonMessage(jsonEx);
                        await NackAsync(ea, requeue: false);
                    }
                    catch (Exception ex)
                    {
                        LogSystemError(ex);

                        var retryCount = GetRetryCount(ea);

                        if (retryCount >= MaxRetryCount)
                        {
                            LogMaxRetriesExceeded(transactionId, retryCount);
                            await NackAsync(ea, requeue: false);
                        }
                        else
                        {
                            var properties = new BasicProperties
                            {
                                Persistent = true,
                                Headers = new Dictionary<string, object?>(
                                    ea.BasicProperties?.Headers ?? new Dictionary<string, object?>())
                            };
                            properties.Headers[RetryHeader] = retryCount + 1;

                            if (_channel?.IsOpen == true)
                            {
                                await _channel.BasicPublishAsync(
                                    exchange: "",
                                    routingKey: ea.RoutingKey,
                                    mandatory: true,
                                    basicProperties: properties,
                                    body: ea.Body);
                            }
                            else
                            {
                                LogChannelClosed(transactionId);
                            }

                            await NackAsync(ea, requeue: false);
                        }
                    }
                };

                await _channel.BasicConsumeAsync(
                    queue: MessagingConstants.OrdersQueue,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                await Task.WhenAny(
                    Task.Delay(Timeout.Infinite, stoppingToken),
                    channelClosedTcs.Task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogRabbitMQRetry(ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessTransactionAsync(TransactionCreatedEvent evt)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeContext>();

        await Task.Delay(500);

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

            var json = JsonSerializer.Serialize(update);
            var body = Encoding.UTF8.GetBytes(json);

            await _channel!.BasicPublishAsync(
                exchange: MessagingConstants.NotificationsExchange,
                routingKey: string.Empty,
                mandatory: false,
                body: body);
        }
        else if (rowsAffected == 0)
        {
            var exists = await dbContext.Transactions.AnyAsync(t => t.Id == evt.TransactionId);
            if (exists)
                LogTransactionAlreadyProcessed(evt.TransactionId, TransactionStatus.Processed);
            else
                LogTransactionNotFound(evt.TransactionId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var channelToClose = Interlocked.Exchange(ref _channel, null);

        if (channelToClose != null)
        {
            try
            {
                await channelToClose.CloseAsync(cancellationToken);
                await channelToClose.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogChannelDisposeError(ex);
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task AckAsync(BasicDeliverEventArgs ea)
    {
        if (_channel?.IsOpen == true) await _channel.BasicAckAsync(ea.DeliveryTag, false);
    }

    private async Task NackAsync(BasicDeliverEventArgs ea, bool requeue)
    {
        if (_channel?.IsOpen == true) await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: requeue);
    }

    private static int GetRetryCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties?.Headers != null
            && ea.BasicProperties.Headers.TryGetValue(RetryHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var count) ? count : 0,
                _ => 0
            };
        }
        return ea.Redelivered ? 1 : 0;
    }

    [LoggerMessage(LogLevel.Information, "Attempting to connect to RabbitMQ...")] private partial void LogConnecting();
    [LoggerMessage(LogLevel.Information, " [*] Connected! Waiting for messages.")] private partial void LogConnected();
    [LoggerMessage(LogLevel.Information, " [x] Processing {TransactionId}")] private partial void LogProcessingMsg(Guid transactionId);
    [LoggerMessage(LogLevel.Error, "Poison Message Detected (Bad JSON). Dropping message.")] private partial void LogPoisonMessage(JsonException ex);
    [LoggerMessage(LogLevel.Error, "System Error while processing message.")] private partial void LogSystemError(Exception ex);
    [LoggerMessage(LogLevel.Error, "RabbitMQ Error. Retrying in 5s...")] private partial void LogRabbitMQRetry(Exception ex);
    [LoggerMessage(LogLevel.Warning, "Channel Shutdown: {ReplyText}")] private partial void LogChannelShutdown(string replyText);
    [LoggerMessage(LogLevel.Warning, " [!] Empty GUID received. Dropping.")] private partial void LogEmptyGuid();
    [LoggerMessage(LogLevel.Information, " [v] Transaction {TransactionId} processed.")] private partial void LogTransactionProcessed(Guid transactionId);
    [LoggerMessage(LogLevel.Warning, " [!] Transaction {TransactionId} not found.")] private partial void LogTransactionNotFound(Guid transactionId);
    [LoggerMessage(LogLevel.Information, " [~] Transaction {TransactionId} already in state '{Status}'. Skipping (idempotent).")] private partial void LogTransactionAlreadyProcessed(Guid transactionId, string status);
    [LoggerMessage(LogLevel.Error, " [!] Transaction {TransactionId} exceeded max retries ({RetryCount}). Sent to DLQ.")] private partial void LogMaxRetriesExceeded(Guid transactionId, int retryCount);
    [LoggerMessage(LogLevel.Warning, " [!] Channel was closed when attempting to republish retry for Tx {TransactionId}. Message may be lost or nacked.")]
    private partial void LogChannelClosed(Guid transactionId);

    [LoggerMessage(LogLevel.Error, "Error disposing RabbitMQ channel during shutdown.")]
    private partial void LogChannelDisposeError(Exception ex);
}