using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TradePlatform.Api.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Tests;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Tests.Api;

public class AccountActivityProjectionHandlerTests(SqlServerTestDatabaseFixture fixture)
    : IClassFixture<SqlServerTestDatabaseFixture>
{
    private readonly SqlServerTestDatabaseFixture _fixture = fixture;

    [Fact]
    public async Task Handle_SubmittedEvent_Should_Create_Outgoing_And_Incoming_ProjectionRows()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var submittedAt = DateTime.UtcNow;

        await handler.Handle(new TransactionSubmittedEvent(
            Guid.NewGuid(),
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            submittedAt));

        var rows = await context.AccountActivityProjections
            .OrderBy(p => p.AccountId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.AccountId == "ACC-100" && row.Direction == AccountActivityDirection.Outgoing);
        Assert.Contains(rows, row => row.AccountId == "ACC-200" && row.Direction == AccountActivityDirection.Incoming);
        Assert.All(rows, row => Assert.Equal(TransactionStatus.Pending, row.Status));
    }

    [Fact]
    public async Task Handle_ProcessedEvent_Should_Update_Existing_ProjectionRows()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-2);
        var processedAt = DateTime.UtcNow;

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionProcessedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            TransactionStatus.Processed,
            processedAt));

        var rows = await context.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(TransactionStatus.Processed, row.Status);
            Assert.Equal(processedAt, row.ProcessedAtUtc);
            Assert.Equal(processedAt, row.LastEventUtc);
        });
    }

    [Fact]
    public async Task Handle_ProcessedEvent_Should_Create_ProjectionRows_When_SubmittedEvent_Has_Not_Arrived_Yet()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var processedAt = DateTime.UtcNow;

        await handler.Handle(new TransactionProcessedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            TransactionStatus.Processed,
            processedAt));

        var rows = await context.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .OrderBy(p => p.AccountId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(TransactionStatus.Processed, row.Status);
            Assert.Equal(processedAt, row.ProcessedAtUtc);
            Assert.Equal(processedAt, row.LastEventUtc);
        });
    }

    [Fact]
    public async Task Handle_ProcessedEvent_Should_Be_Idempotent_When_Status_Is_Already_Terminal()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-2);
        var firstProcessedAt = DateTime.UtcNow.AddSeconds(-1);
        var duplicateProcessedAt = DateTime.UtcNow;

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionProcessedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            TransactionStatus.Processed,
            firstProcessedAt));

        await handler.Handle(new TransactionProcessedEvent(
            transactionId,
            "ACC-100",
            "ACC-200",
            150m,
            "USD",
            TransactionStatus.Processed,
            duplicateProcessedAt));

        var rows = await context.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.All(rows, row =>
        {
            Assert.Equal(TransactionStatus.Processed, row.Status);
            Assert.Equal(firstProcessedAt, row.ProcessedAtUtc);
            Assert.Equal(firstProcessedAt, row.LastEventUtc);
        });
    }

    private static AccountActivityProjectionHandler CreateHandler(TradeContext context)
    {
        return new AccountActivityProjectionHandler(
            context,
            Mock.Of<ILogger<AccountActivityProjectionHandler>>());
    }
}
