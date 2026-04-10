using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Infrastructure.Services
{
    public class SqlMessageInbox : IMessageInbox
    {
        public async Task<bool> TryBeginProcessingAsync(
            ITradeContext context,
            string consumer,
            string messageId,
            CancellationToken cancellationToken = default)
        {
            context.InboxMessages.Add(new InboxMessage
            {
                MessageId = messageId,
                Consumer = consumer,
                ProcessedAtUtc = DateTime.UtcNow
            });

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                if (context is DbContext dbContext)
                {
                    dbContext.ChangeTracker.Clear();
                }

                return false;
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }
    }
}
