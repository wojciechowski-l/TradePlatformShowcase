using TradePlatform.Core.Entities;

namespace TradePlatform.Core.Interfaces
{
    public interface IMessageInbox
    {
        Task<bool> TryBeginProcessingAsync(
            ITradeContext context,
            string consumer,
            string messageId,
            CancellationToken cancellationToken = default);
    }
}
