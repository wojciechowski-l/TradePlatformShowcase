using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs
{
    public class TransactionUpdateDto
    {
        public Guid TransactionId { get; set; }
        public TransactionStatus Status { get; set; }
        public string AccountId { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }
}