using RabbitMQ.Client;
using TradePlatform.Core.Constants;

namespace TradePlatform.Infrastructure.Messaging
{
    public class RabbitMQTopologySetup
    {
        public static async Task InitializeAsync(IRabbitMQConnection connection)
        {
            using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(
                queue: MessagingConstants.OrdersQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", MessagingConstants.OrdersDeadLetterExchange },
                    { "x-dead-letter-routing-key", MessagingConstants.OrdersDeadLetterRoutingKey }
                }
            );

            await channel.ExchangeDeclareAsync(
                exchange: MessagingConstants.OrdersDeadLetterExchange,
                type: ExchangeType.Direct,
                durable: true
            );

            await channel.QueueDeclareAsync(
                queue: MessagingConstants.OrdersDeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            await channel.QueueBindAsync(
                queue: MessagingConstants.OrdersDeadLetterQueue,
                exchange: MessagingConstants.OrdersDeadLetterExchange,
                routingKey: MessagingConstants.OrdersDeadLetterRoutingKey
            );

            await channel.ExchangeDeclareAsync(
                exchange: MessagingConstants.NotificationsExchange,
                type: ExchangeType.Fanout,
                durable: true
            );

            await channel.QueueDeclareAsync(
                queue: MessagingConstants.NotificationsQueue,
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            await channel.QueueBindAsync(
                queue: MessagingConstants.NotificationsQueue,
                exchange: MessagingConstants.NotificationsExchange,
                routingKey: string.Empty
            );
        }
    }
}