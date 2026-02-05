namespace TradePlatform.Core.Entities
{
    public enum OutboxStatus
    {
        Pending = 0,
        InFlight = 1,
        Processed = 2,
        Failed = 3
    }
}
