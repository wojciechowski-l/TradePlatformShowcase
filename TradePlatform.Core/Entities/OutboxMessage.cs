namespace TradePlatform.Core.Entities
{
    public class OutboxMessage
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
        public DateTime? LastAttemptAtUtc { get; set; }
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
    }
}
