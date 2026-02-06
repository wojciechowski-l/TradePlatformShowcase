using TradePlatform.Core.Constants;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Core.Entities
{
    public class TransactionRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SourceAccountId { get; set; } = string.Empty;
        public virtual Account? SourceAccount { get; set; }
        public string TargetAccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public required Currency Currency { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    }
}