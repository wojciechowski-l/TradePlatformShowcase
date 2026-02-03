namespace TradePlatform.Core.DTOs
{
    public class TransactionUpdateDto
    {
        public Guid TransactionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }
}