using Rebus.Messages;
using Rebus.Pipeline;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Infrastructure.Services
{
    public class RebusMessageMetadataAccessor : IMessageMetadataAccessor
    {
        public string? GetCurrentMessageId()
        {
            var messageContext = MessageContext.Current;

            if (messageContext?.Headers is null)
            {
                return null;
            }

            return messageContext.Headers.TryGetValue(Headers.MessageId, out var messageId)
                ? messageId
                : null;
        }

        public int GetCurrentDeliveryCount()
        {
            var messageContext = MessageContext.Current;

            if (messageContext?.Headers is null)
            {
                return 1;
            }

            return messageContext.Headers.TryGetValue(Headers.DeliveryCount, out var deliveryCount)
                && int.TryParse(deliveryCount, out var parsedDeliveryCount)
                    ? parsedDeliveryCount
                    : 1;
        }
    }
}
