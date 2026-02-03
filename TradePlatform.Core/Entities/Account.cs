using System.ComponentModel.DataAnnotations;

namespace TradePlatform.Core.Entities
{
    public class Account
    {
        [Key]
        [MaxLength(50)]
        public string Id { get; set; } = string.Empty;

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        // Navigation property
        public ApplicationUser? Owner { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        // Optional: Track balance if needed later
        // public decimal Balance { get; set; }
    }
}