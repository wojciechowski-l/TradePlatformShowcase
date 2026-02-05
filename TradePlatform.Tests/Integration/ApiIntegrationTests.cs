using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Testcontainers.MsSql;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Tests.Integration;

public class ApiIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                builder.ConfigureTestServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<TradeContext>));

                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddDbContext<TradeContext>(options =>
                        options.UseSqlServer(_dbContainer.GetConnectionString()));

                    services.AddAuthentication("TestScheme")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            "TestScheme", _ => { });
                });
            });
    }

    [Fact]
    public async Task CreateTransaction_Should_Return_Accepted()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");

        var request = new TransactionDto
        {
            SourceAccountId = "ACC_1",
            TargetAccountId = "ACC_2",
            Amount = 100,
            Currency = "USD"
        };

        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            request,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateTransactionResult>(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(TransactionStatus.Pending, result.Status);
    }

    [Fact]
    public async Task CreateTransaction_Should_Validate_Inputs()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");

        var request = new TransactionDto
        {
            SourceAccountId = "SAME",
            TargetAccountId = "SAME",
            Amount = 100,
            Currency = "USD"
        };

        var response = await client.PostAsJsonAsync(
            "/api/transactions",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Contains("Source and Target accounts must be different", body);
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[] { new Claim(ClaimTypes.Name, "IntegrationTestUser") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}