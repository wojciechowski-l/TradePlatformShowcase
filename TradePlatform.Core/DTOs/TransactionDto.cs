namespace TradePlatform.Core.DTOs
{
    public class TransactionDto
    {
        public string SourceAccountId { get; set; } = string.Empty;
        public string TargetAccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}