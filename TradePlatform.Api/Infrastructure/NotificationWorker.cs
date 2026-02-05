using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Messaging;

namespace TradePlatform.Api.Infrastructure
{
    public partial class NotificationWorker(
        IRabbitMQConnection mqConnection,
        IHubContext<TradeHub> hubContext,
        ILogger<NotificationWorker> logger
    ) : BackgroundService
    {
        private IChannel? _channel;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _channel = await mqConnection.CreateChannelAsync();

                    var consumer = new AsyncEventingBasicConsumer(_channel);
                    consumer.ReceivedAsync += async (model, ea) =>
                    {
                        try
                        {
                            var body = ea.Body.ToArray();
                            var json = Encoding.UTF8.GetString(body);
                            var update = JsonSerializer.Deserialize<TransactionUpdateDto>(json);

                            if (update != null && !string.IsNullOrEmpty(update.AccountId))
                            {
                                await hubContext.Clients.Group(update.AccountId)
                                    .SendAsync("ReceiveStatusUpdate", update, cancellationToken: stoppingToken);

                                LogUpdatePushed(update.TransactionId, update.AccountId);
                            }

                            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                        }
                        catch (Exception ex)
                        {
                            LogProcessingError(ex);
                            if (_channel.IsOpen)
                            {
                                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                            }
                        }
                    };

                    await _channel.BasicConsumeAsync(
                        queue: MessagingConstants.NotificationsQueue,
                        autoAck: false,
                        consumer: consumer,
                        cancellationToken: stoppingToken);

                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogConnectionError(ex);
                    await Task.Delay(5000, stoppingToken);
                }
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

        [LoggerMessage(LogLevel.Information, "Pushed update for Tx {Id} to Account {Acc}")]
        private partial void LogUpdatePushed(Guid id, string acc);

        [LoggerMessage(LogLevel.Error, "Error processing notification")]
        private partial void LogProcessingError(Exception ex);

        [LoggerMessage(LogLevel.Warning, "Failed to connect to RabbitMQ in NotificationWorker. Retrying in 5s...")]
        private partial void LogConnectionError(Exception ex);
        [LoggerMessage(LogLevel.Error, "Error disposing RabbitMQ channel during shutdown.")]
        private partial void LogChannelDisposeError(Exception ex);
    }
}