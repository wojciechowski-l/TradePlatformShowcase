using TradePlatform.Core.Constants;

namespace TradePlatform.Core.Entities
{
    public class TransactionRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SourceAccountId { get; set; } = string.Empty;
        public string TargetAccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = TransactionStatus.Pending; // Pending, Processed, Failed
    }
}