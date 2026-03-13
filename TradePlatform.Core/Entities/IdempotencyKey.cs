namespace TradePlatform.Core.Entities
{
    public class IdempotencyKey
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public Guid TransactionId { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}