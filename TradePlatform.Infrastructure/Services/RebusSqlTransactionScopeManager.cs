using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rebus.Config.Outbox;
using Rebus.Transport;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public class RebusSqlTransactionScopeManager(TradeContext dbContext) : ITransactionScopeManager
    {
        public async Task ExecuteInTransactionAsync(Func<Task> action)
        {
            await ExecuteInTransactionAsync<int>(async () =>
            {
                await action();
                return 0;
            });
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action)
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync();

            var dbConnection = dbContext.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            using var rebusScope = new RebusTransactionScope();

            rebusScope.UseOutbox((SqlConnection)dbConnection, (SqlTransaction)dbTransaction);

            var result = await action();

            await rebusScope.CompleteAsync();

            await transaction.CommitAsync();

            return result;
        }
    }
}