namespace TradePlatform.Core.Interfaces
{
    public interface IAccountActivityProjectionRebuilder
    {
        Task<int> RebuildAsync(CancellationToken cancellationToken = default);
    }
}
