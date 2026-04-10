namespace TradePlatform.Core.Entities
{
    public class InboxMessage
    {
        public long Id { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public string Consumer { get; set; } = string.Empty;
        public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
