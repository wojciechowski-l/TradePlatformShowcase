namespace TradePlatform.Core.Interfaces
{
    public interface IMessageProducer
    {
        Task SendMessageAsync<T>(T message);
    }
}
