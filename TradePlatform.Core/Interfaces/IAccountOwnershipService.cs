namespace TradePlatform.Core.Interfaces
{
    public interface IAccountOwnershipService
    {
        Task<bool> IsOwnerAsync(string userId, string accountId, CancellationToken cancellationToken = default);
    }
}