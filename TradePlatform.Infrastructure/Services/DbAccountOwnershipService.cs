using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public class DbAccountOwnershipService(TradeContext context, IMemoryCache cache) : IAccountOwnershipService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

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

            var cacheKey = $"ownership:{userId}:{accountId}";

            return await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;

                return await context.Accounts
                    .AsNoTracking()
                    .AnyAsync(a => a.Id == accountId && a.OwnerId == userId, cancellationToken);
            });
        }
    }
}