using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TradePlatform.Core.Constants;
using TradePlatform.Core.Entities;
using TradePlatform.Tests;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;

namespace TradePlatform.Tests.Infrastructure;

public class AccountActivityProjectionRebuilderTests(SqlServerTestDatabaseFixture fixture)
    : IClassFixture<SqlServerTestDatabaseFixture>
{
    private readonly SqlServerTestDatabaseFixture _fixture = fixture;

    [Fact]
    public async Task RebuildAsync_Should_Recreate_Projection_From_Transactions()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"projection-rebuild-{Guid.NewGuid()}",
            Email = $"projection-rebuild-{Guid.NewGuid()}@example.com",
            FullName = "Projection Test User"
        };

        context.Users.Add(user);
        context.Accounts.AddRange(
            new Account
            {
                Id = "ACC-100",
                OwnerId = user.Id,
                Currency = Currency.FromCode("USD")
            },
            new Account
            {
                Id = "ACC-200",
                OwnerId = user.Id,
                Currency = Currency.FromCode("USD")
            },
            new Account
            {
                Id = "ACC-300",
                OwnerId = user.Id,
                Currency = Currency.FromCode("EUR")
            },
            new Account
            {
                Id = "ACC-400",
                OwnerId = user.Id,
                Currency = Currency.FromCode("EUR")
            });

        context.Transactions.AddRange(
            new TransactionRecord
            {
                Id = Guid.NewGuid(),
                SourceAccountId = "ACC-100",
                TargetAccountId = "ACC-200",
                Amount = 50m,
                Currency = Currency.FromCode("USD"),
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                Status = TransactionStatus.Pending
            },
            new TransactionRecord
            {
                Id = Guid.NewGuid(),
                SourceAccountId = "ACC-300",
                TargetAccountId = "ACC-400",
                Amount = 75m,
                Currency = Currency.FromCode("EUR"),
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                Status = TransactionStatus.Processed
            });

        context.AccountActivityProjections.Add(new AccountActivityProjection
        {
            TransactionId = Guid.NewGuid(),
            AccountId = "STALE",
            CounterpartyAccountId = "STALE",
            Direction = AccountActivityDirection.Outgoing,
            Amount = 1m,
            Currency = "USD",
            Status = TransactionStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            LastEventUtc = DateTime.UtcNow.AddHours(-1)
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rebuilder = new AccountActivityProjectionRebuilder(
            context,
            Mock.Of<ILogger<AccountActivityProjectionRebuilder>>());

        var rowCount = await rebuilder.RebuildAsync(TestContext.Current.CancellationToken);

        var rows = await context.AccountActivityProjections
            .OrderBy(p => p.AccountId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, rowCount);
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, row => row.AccountId == "STALE");
        Assert.Contains(rows, row => row.AccountId == "ACC-100" && row.Direction == AccountActivityDirection.Outgoing);
        Assert.Contains(rows, row => row.AccountId == "ACC-200" && row.Direction == AccountActivityDirection.Incoming);
        Assert.Contains(rows, row => row.AccountId == "ACC-300" && row.Status == TransactionStatus.Processed && row.ProcessedAtUtc.HasValue);
    }
}
