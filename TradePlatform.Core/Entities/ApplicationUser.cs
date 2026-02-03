using Microsoft.AspNetCore.Identity;

namespace TradePlatform.Core.Entities
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public ICollection<Account> Accounts { get; set; } = [];
    }
}