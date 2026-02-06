using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TradePlatform.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Infrastructure.Data
{
    public class TradeContext(DbContextOptions<TradeContext> options) : IdentityDbContext<ApplicationUser>(options), ITradeContext
    {
        public DbSet<TransactionRecord> Transactions { get; set; }
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
                .HasPrecision(18, 2);

            modelBuilder.Entity<TransactionRecord>()
                .Property(t => t.SourceAccountId)
                .IsRequired()
                .HasMaxLength(50);

            modelBuilder.Entity<TransactionRecord>()
                .HasOne(t => t.SourceAccount)
                .WithMany()
                .HasForeignKey(t => t.SourceAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TransactionRecord>()
                .Property(t => t.Status)
                .HasConversion<string>();

            modelBuilder.Entity<TransactionRecord>()
                .Property(t => t.Currency)
                .HasConversion(c => c.Code, s => Currency.FromCode(s))
                .HasMaxLength(3);

            modelBuilder.Entity<Account>()
                .HasKey(a => a.Id);

            modelBuilder.Entity<Account>()
                .HasOne(a => a.Owner)
                .WithMany(u => u.Accounts)
                .HasForeignKey(a => a.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Account>()
                .HasIndex(a => new { a.OwnerId, a.Id });

            modelBuilder.Entity<Account>()
                .Property(a => a.Currency)
                .HasConversion(c => c.Code, s => Currency.FromCode(s))
                .HasMaxLength(3);
        }
    }
}