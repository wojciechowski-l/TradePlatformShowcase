using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs
{
    public class AccountActivityDto
    {
        public Guid TransactionId { get; set; }
        public string AccountId { get; set; } = string.Empty;
        public string CounterpartyAccountId { get; set; } = string.Empty;
        public AccountActivityDirection Direction { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public TransactionStatus Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ProcessedAtUtc { get; set; }
        public DateTime LastEventUtc { get; set; }
    }
}
