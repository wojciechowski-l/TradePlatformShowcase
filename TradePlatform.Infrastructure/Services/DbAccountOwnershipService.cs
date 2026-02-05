using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public class DbAccountOwnershipService(TradeContext context) : IAccountOwnershipService
    {
        public async Task<bool> IsOwnerAsync(ClaimsPrincipal user, string accountId, CancellationToken cancellationToken = default)
        {
            if (user == null || string.IsNullOrWhiteSpace(accountId)) return false;

            var accountIdClaim = user.FindFirst("urn:tradeplatform:accountid")?.Value;
            if (string.Equals(accountIdClaim, accountId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId)) return false;

            return await context.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId && a.OwnerId == userId, cancellationToken);
        }
    }
}