using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.Constants;

namespace TradePlatform.Infrastructure.Messaging
{
    public class RabbitMQProducer(IRabbitMQConnection connectionService) : IMessageProducer
    {
        private readonly IRabbitMQConnection _connectionService = connectionService;

        public async Task SendMessageAsync<T>(T message)
        {
            using var channel = await _connectionService.CreateChannelAsync();

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);
            var properties = new BasicProperties { Persistent = true };

            await channel.BasicPublishAsync("", MessagingConstants.OrdersQueue, false, properties, body);
        }
    }
}