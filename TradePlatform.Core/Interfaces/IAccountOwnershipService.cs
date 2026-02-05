using System.Security.Claims;

namespace TradePlatform.Core.Interfaces
{
    public interface IAccountOwnershipService
    {
        Task<bool> IsOwnerAsync(ClaimsPrincipal user, string accountId, CancellationToken cancellationToken = default);
    }
}