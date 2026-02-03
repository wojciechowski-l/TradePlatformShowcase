using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TradePlatform.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Infrastructure.Data
{
    public class TradeContext(DbContextOptions<TradeContext> options) : IdentityDbContext<ApplicationUser>(options), ITradeContext
    {
        public DbSet<TransactionRecord> Transactions { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<Account> Accounts { get; set; }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Database.BeginTransactionAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TransactionRecord>()
                .HasKey(t => t.Id);

            modelBuilder.Entity<TransactionRecord>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2); // Important for money!

            modelBuilder.Entity<TransactionRecord>()
                .Property(t => t.SourceAccountId)
                .IsRequired()
                .HasMaxLength(50);

            modelBuilder.Entity<OutboxMessage>()
                .HasKey(o => o.Id);

            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(o => new { o.ProcessedAtUtc, o.AttemptCount, o.CreatedAtUtc });

            modelBuilder.Entity<Account>()
                .HasKey(a => a.Id);

            modelBuilder.Entity<Account>()
                .HasOne(a => a.Owner)
                .WithMany(u => u.Accounts)
                .HasForeignKey(a => a.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Account>()
                .HasIndex(a => new { a.OwnerId, a.Id });
        }
    }
}