using System.ComponentModel.DataAnnotations;
using TradePlatform.Core.Constants;

namespace TradePlatform.Core.Entities
{
    public class AccountActivityProjection
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid TransactionId { get; set; }

        [Required]
        [MaxLength(50)]
        public string AccountId { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string CounterpartyAccountId { get; set; } = string.Empty;

        [Required]
        public AccountActivityDirection Direction { get; set; }

        public decimal Amount { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = string.Empty;

        [Required]
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? ProcessedAtUtc { get; set; }

        public DateTime LastEventUtc { get; set; }

        [MaxLength(250)]
        public string? FailureReason { get; set; }
    }
}
