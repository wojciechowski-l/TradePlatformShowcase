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
        var processedAt = DateTime.UtcNow.AddSeconds(-20);

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
                Status = TransactionStatus.Processed,
                CompletedAtUtc = processedAt
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
        Assert.Contains(rows, row => row.AccountId == "ACC-300" && row.Status == TransactionStatus.Processed && row.ProcessedAtUtc == processedAt);
    }

    [Fact]
    public async Task RebuildAsync_Should_Preserve_Failure_Metadata_And_Skip_Missing_Target_Accounts()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"projection-rebuild-failed-{Guid.NewGuid()}",
            Email = $"projection-rebuild-failed-{Guid.NewGuid()}@example.com",
            FullName = "Projection Failure Test User"
        };
        var completedAt = DateTime.UtcNow.AddSeconds(-10);

        context.Users.Add(user);
        context.Accounts.Add(new Account
        {
            Id = "ACC-500",
            OwnerId = user.Id,
            Currency = Currency.FromCode("USD")
        });

        var failedTransactionId = Guid.NewGuid();
        context.Transactions.Add(new TransactionRecord
        {
            Id = failedTransactionId,
            SourceAccountId = "ACC-500",
            TargetAccountId = "ACC-MISSING",
            Amount = 25m,
            Currency = Currency.FromCode("USD"),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            Status = TransactionStatus.Failed,
            CompletedAtUtc = completedAt,
            FailureReason = "Target account does not exist."
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rebuilder = new AccountActivityProjectionRebuilder(
            context,
            Mock.Of<ILogger<AccountActivityProjectionRebuilder>>());

        var rowCount = await rebuilder.RebuildAsync(TestContext.Current.CancellationToken);

        var rows = await context.AccountActivityProjections
            .Where(p => p.TransactionId == failedTransactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rowCount);
        Assert.Single(rows);
        Assert.Equal("ACC-500", rows[0].AccountId);
        Assert.Equal(AccountActivityDirection.Outgoing, rows[0].Direction);
        Assert.Equal(TransactionStatus.Failed, rows[0].Status);
        Assert.Equal(completedAt, rows[0].ProcessedAtUtc);
        Assert.Equal(completedAt, rows[0].LastEventUtc);
        Assert.Equal("Target account does not exist.", rows[0].FailureReason);
    }
}
