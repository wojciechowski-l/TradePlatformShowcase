using System.ComponentModel.DataAnnotations;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Core.Entities
{
    public class Account
    {
        [Key]
        [MaxLength(50)]
        public string Id { get; set; } = string.Empty;

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        public ApplicationUser? Owner { get; set; }

        public required Currency Currency { get; set; }

        // Optional: Track balance if needed later
        // public decimal Balance { get; set; }
    }
}