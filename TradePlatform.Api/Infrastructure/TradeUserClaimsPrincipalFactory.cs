using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Api.Infrastructure
{
    public class TradeUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        IOptions<IdentityOptions> optionsAccessor,
        TradeContext dbContext)
        : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
    {
        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);

            var account = await dbContext.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.OwnerId == user.Id);

            if (account != null)
            {
                identity.AddClaim(new Claim("urn:tradeplatform:accountid", account.Id));
            }

            return identity;
        }
    }
}