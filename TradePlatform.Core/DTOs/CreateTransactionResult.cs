namespace TradePlatform.Core.DTOs
{
    public class CreateTransactionResult
    {
        public Guid TransactionId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}