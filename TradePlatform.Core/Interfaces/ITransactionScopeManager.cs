namespace TradePlatform.Core.Interfaces
{
    public interface ITransactionScopeManager
    {
        Task ExecuteInTransactionAsync(Func<Task> action);
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action);
    }
}
