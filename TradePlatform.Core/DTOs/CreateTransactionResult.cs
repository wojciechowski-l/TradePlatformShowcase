using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs
{
    public class CreateTransactionResult
    {
        public Guid TransactionId { get; set; }
        public TransactionStatus Status { get; set; }
    }
}