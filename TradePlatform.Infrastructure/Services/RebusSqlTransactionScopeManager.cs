using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rebus.Config.Outbox;
using Rebus.Transport;
using System.Threading;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Infrastructure.Services
{
    public class RebusSqlTransactionScopeManager(TradeContext dbContext) : ITransactionScopeManager
    {
        public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await ExecuteInTransactionAsync<int>(async () =>
            {
                await action();
                return 0;
            }, cancellationToken);
        }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var dbConnection = dbContext.Database.GetDbConnection();
            var dbTransaction = transaction.GetDbTransaction();

            using var rebusScope = new RebusTransactionScope();

            rebusScope.UseOutbox((SqlConnection)dbConnection, (SqlTransaction)dbTransaction);

            var result = await action();

            await rebusScope.CompleteAsync();

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
    }
}