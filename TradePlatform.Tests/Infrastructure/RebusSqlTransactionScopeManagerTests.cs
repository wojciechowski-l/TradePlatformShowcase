using Microsoft.EntityFrameworkCore;
using TradePlatform.Core.Entities;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;

namespace TradePlatform.Tests.Infrastructure;

public class RebusSqlTransactionScopeManagerTests(SqlServerTestDatabaseFixture fixture)
    : IClassFixture<SqlServerTestDatabaseFixture>
{
    private readonly SqlServerTestDatabaseFixture _fixture = fixture;

    [Fact]
    public async Task ExecuteInTransactionAsync_Should_Commit_Database_Changes_When_Action_Completes()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        await EnsureOutboxTableAsync(context);

        var manager = new RebusSqlTransactionScopeManager(context);
        var userId = Guid.NewGuid().ToString();

        await manager.ExecuteInTransactionAsync(async () =>
        {
            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"commit-{userId}@test.local",
                Email = $"commit-{userId}@test.local",
                FullName = "Commit User"
            });

            context.Accounts.Add(new Account
            {
                Id = "ACC-COMMIT",
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        await using var verificationContext = CreateContextForSameDatabase(context);
        var savedAccount = await verificationContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == "ACC-COMMIT", TestContext.Current.CancellationToken);

        Assert.NotNull(savedAccount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Should_Roll_Back_Database_Changes_When_Action_Throws()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        await EnsureOutboxTableAsync(context);

        var manager = new RebusSqlTransactionScopeManager(context);
        var userId = Guid.NewGuid().ToString();

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ExecuteInTransactionAsync(async () =>
        {
            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"rollback-{userId}@test.local",
                Email = $"rollback-{userId}@test.local",
                FullName = "Rollback User"
            });

            context.Accounts.Add(new Account
            {
                Id = "ACC-ROLLBACK",
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            throw new InvalidOperationException("boom");
        }, TestContext.Current.CancellationToken));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var savedAccount = await verificationContext.Accounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == "ACC-ROLLBACK", TestContext.Current.CancellationToken);

        Assert.Null(savedAccount);
    }

    private static async Task EnsureOutboxTableAsync(TradeContext context)
    {
        var createOutboxSql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RebusOutbox')
            CREATE TABLE [dbo].[RebusOutbox] (
                [id] [bigint] IDENTITY(1,1) NOT NULL,
                [message_id] [nvarchar](200) NOT NULL,
                [source_queue] [nvarchar](200) NOT NULL,
                [destination_queue] [nvarchar](200) NOT NULL,
                [headers] [varbinary](max) NULL,
                [body] [varbinary](max) NULL,
                [creation_time] [datetimeoffset](7) NOT NULL,
                CONSTRAINT [PK_RebusOutbox] PRIMARY KEY CLUSTERED ([id] ASC)
            );";

        await context.Database.ExecuteSqlRawAsync(createOutboxSql, TestContext.Current.CancellationToken);
    }

    private static TradeContext CreateContextForSameDatabase(TradeContext existingContext)
    {
        var connectionString = existingContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Missing connection string for verification context.");

        var options = new DbContextOptionsBuilder<TradeContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TradeContext(options);
    }
}
