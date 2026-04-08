using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using TradePlatform.Core.Constants;
using TradePlatform.Core.Entities;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;

namespace TradePlatform.Tests.Infrastructure;

public class DbAccountOwnershipServiceTests
{
    [Fact]
    public async Task IsOwnerAsync_Should_Return_True_When_Account_Claim_Matches_Without_Database_Row()
    {
        await using var context = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DbAccountOwnershipService(context, cache);
        var principal = CreatePrincipal("user-1", ("urn:ignored", "ignored"), (TradeClaimTypes.AccountId, "ACC-123"));

        var result = await service.IsOwnerAsync(principal, "ACC-123", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task IsOwnerAsync_Should_Fall_Back_To_Database_And_Cache_Positive_Result()
    {
        await using var context = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var userId = "user-2";

        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "owner@test.local",
            Email = "owner@test.local",
            FullName = "Owner"
        });
        context.Accounts.Add(new Account
        {
            Id = "ACC-456",
            OwnerId = userId,
            Currency = Currency.FromCode("USD")
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new DbAccountOwnershipService(context, cache);
        var principal = CreatePrincipal(userId);

        var firstResult = await service.IsOwnerAsync(principal, "ACC-456", TestContext.Current.CancellationToken);

        context.Accounts.RemoveRange(context.Accounts);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var secondResult = await service.IsOwnerAsync(principal, "ACC-456", TestContext.Current.CancellationToken);

        Assert.True(firstResult);
        Assert.True(secondResult);
    }

    [Fact]
    public async Task IsOwnerAsync_Should_Return_False_When_User_Is_Missing()
    {
        await using var context = CreateContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new DbAccountOwnershipService(context, cache);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await service.IsOwnerAsync(principal, "ACC-789", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    private static TradeContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TradeContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TradeContext(options);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, params (string Type, string Value)[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        claims.AddRange(extraClaims.Select(claim => new Claim(claim.Type, claim.Value)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
