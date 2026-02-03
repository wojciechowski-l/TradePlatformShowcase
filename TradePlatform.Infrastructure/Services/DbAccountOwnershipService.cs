using Microsoft.EntityFrameworkCore;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public class DbAccountOwnershipService(TradeContext context) : IAccountOwnershipService
    {
        public async Task<bool> IsOwnerAsync(string userId, string accountId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(accountId))
                return false;

            return await context.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == accountId && a.OwnerId == userId, cancellationToken);
        }
    }
}