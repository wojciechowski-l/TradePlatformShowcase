using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using TradePlatform.Api.Handlers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Tests;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;

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
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            Guid.NewGuid(),
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        var rows = await context.AccountActivityProjections
            .OrderBy(p => p.AccountId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.AccountId == sourceAccountId && row.Direction == AccountActivityDirection.Outgoing);
        Assert.Contains(rows, row => row.AccountId == targetAccountId && row.Direction == AccountActivityDirection.Incoming);
        Assert.All(rows, row => Assert.Equal(TransactionStatus.Pending, row.Status));
    }

    [Fact]
    public async Task Handle_StatusChangedEvent_Should_Update_Existing_ProjectionRows()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-2);
        var processedAt = DateTime.UtcNow;
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Processing,
            TransactionStatus.Processed,
            processedAt));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var rows = await verificationContext.AccountActivityProjections
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
    public async Task Handle_StatusChangedEvent_Should_Create_ProjectionRows_When_SubmittedEvent_Has_Not_Arrived_Yet()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var processedAt = DateTime.UtcNow;
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Processing,
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
    public async Task Handle_StatusChangedEvent_Should_Be_Idempotent_When_Status_Is_Already_Terminal()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-2);
        var firstProcessedAt = DateTime.UtcNow.AddSeconds(-1);
        var duplicateProcessedAt = DateTime.UtcNow;
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Processing,
            TransactionStatus.Processed,
            firstProcessedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Processing,
            TransactionStatus.Processed,
            duplicateProcessedAt));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var rows = await verificationContext.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.All(rows, row =>
        {
            Assert.Equal(TransactionStatus.Processed, row.Status);
            Assert.Equal(firstProcessedAt, row.ProcessedAtUtc);
            Assert.Equal(firstProcessedAt, row.LastEventUtc);
        });
    }

    [Fact]
    public async Task Handle_SubmittedEvent_Should_Not_Regress_Projection_When_Later_Status_Already_Arrived()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var validatedAt = DateTime.UtcNow;
        var submittedAt = validatedAt.AddSeconds(-5);
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Pending,
            TransactionStatus.Validated,
            validatedAt));

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var rows = await verificationContext.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(TransactionStatus.Validated, row.Status);
            Assert.Equal(validatedAt, row.LastEventUtc);
            Assert.Equal(submittedAt, row.CreatedAtUtc);
        });
    }

    [Fact]
    public async Task Handle_StatusChangedEvent_Should_Not_Regress_When_Lower_Status_Arrives_After_Processed()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-5);
        var processedAt = DateTime.UtcNow;
        var staleProcessingAt = processedAt.AddSeconds(1);
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Processing,
            TransactionStatus.Processed,
            processedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Validated,
            TransactionStatus.Processing,
            staleProcessingAt));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var rows = await verificationContext.AccountActivityProjections
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
    public async Task Handle_StatusChangedEvent_Should_Use_Fresh_Db_State_When_Context_Is_Stale()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow.AddSeconds(-5);
        var processedAt = DateTime.UtcNow;
        var staleProcessingAt = processedAt.AddSeconds(1);
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var targetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];

        await SeedAccountAsync(context, sourceAccountId);
        await SeedAccountAsync(context, targetAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            submittedAt));

        _ = await context.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        await using (var freshContext = CreateContextForSameDatabase(context))
        {
            var freshRows = await freshContext.AccountActivityProjections
                .Where(p => p.TransactionId == transactionId)
                .ToListAsync(TestContext.Current.CancellationToken);

            foreach (var row in freshRows)
            {
                row.Status = TransactionStatus.Processed;
                row.ProcessedAtUtc = processedAt;
                row.LastEventUtc = processedAt;
            }

            await freshContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            targetAccountId,
            150m,
            "USD",
            TransactionStatus.Validated,
            TransactionStatus.Processing,
            staleProcessingAt));

        await using var verificationContext = CreateContextForSameDatabase(context);
        var rows = await verificationContext.AccountActivityProjections
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
    public async Task Handle_FailedTransactionForMissingTarget_Should_Not_Create_IncomingProjection()
    {
        await using var context = await _fixture.CreateContextAsync(TestContext.Current.CancellationToken);
        var handler = CreateHandler(context);
        var transactionId = Guid.NewGuid();
        var sourceAccountId = $"ACC-SRC-{Guid.NewGuid():N}"[..18];
        var missingTargetAccountId = $"ACC-TGT-{Guid.NewGuid():N}"[..18];
        var submittedAt = DateTime.UtcNow.AddSeconds(-2);
        var failedAt = DateTime.UtcNow;

        await SeedAccountAsync(context, sourceAccountId);

        await handler.Handle(new TransactionSubmittedEvent(
            transactionId,
            sourceAccountId,
            missingTargetAccountId,
            150m,
            "USD",
            submittedAt));

        await handler.Handle(new TransactionStatusChangedEvent(
            transactionId,
            sourceAccountId,
            missingTargetAccountId,
            150m,
            "USD",
            TransactionStatus.Pending,
            TransactionStatus.Failed,
            failedAt,
            "Target account does not exist."));

        var rows = await context.AccountActivityProjections
            .Where(p => p.TransactionId == transactionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(rows);
        Assert.Equal(sourceAccountId, rows[0].AccountId);
        Assert.Equal(AccountActivityDirection.Outgoing, rows[0].Direction);
        Assert.Equal(TransactionStatus.Failed, rows[0].Status);
        Assert.Equal("Target account does not exist.", rows[0].FailureReason);
    }

    private static AccountActivityProjectionHandler CreateHandler(TradeContext context)
    {
        var messageMetadataAccessor = new Mock<IMessageMetadataAccessor>();
        messageMetadataAccessor
            .Setup(accessor => accessor.GetCurrentMessageId())
            .Returns(() => Guid.NewGuid().ToString("N"));
        messageMetadataAccessor
            .Setup(accessor => accessor.GetCurrentDeliveryCount())
            .Returns(1);

        return new AccountActivityProjectionHandler(
            new SameDatabaseContextFactory(context),
            new SqlMessageInbox(),
            messageMetadataAccessor.Object,
            Mock.Of<ILogger<AccountActivityProjectionHandler>>());
    }

    private static TradeContext CreateContextForSameDatabase(TradeContext existingContext)
    {
        var connectionString = existingContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Missing connection string for test context.");

        var options = new DbContextOptionsBuilder<TradeContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new TradeContext(options);
    }

    private sealed class SameDatabaseContextFactory(TradeContext existingContext) : IDbContextFactory<TradeContext>
    {
        public TradeContext CreateDbContext() => CreateContextForSameDatabase(existingContext);

        public async Task<TradeContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return CreateDbContext();
        }
    }

    private static async Task SeedAccountAsync(TradeContext context, string accountId)
    {
        var ownerId = Guid.NewGuid().ToString();

        context.Users.Add(new ApplicationUser
        {
            Id = ownerId,
            UserName = $"user-{ownerId}",
            NormalizedUserName = $"USER-{ownerId}".ToUpperInvariant(),
            Email = $"{ownerId}@test.local",
            NormalizedEmail = $"{ownerId}@test.local".ToUpperInvariant(),
            FullName = "Projection Test User"
        });

        context.Accounts.Add(new Account
        {
            Id = accountId,
            OwnerId = ownerId,
            Currency = Currency.FromCode("USD"),
            Balance = 1000m
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
