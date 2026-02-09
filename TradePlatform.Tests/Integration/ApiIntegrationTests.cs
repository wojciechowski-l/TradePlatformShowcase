using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Tests.Integration;

public class TradePlatformTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private readonly RabbitMqContainer _rabbitContainer =
        new RabbitMqBuilder("rabbitmq:4-management")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);
        await _rabbitContainer.StartAsync(TestContext.Current.CancellationToken);

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitContainer.GetConnectionString())
        };

        using var connection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);

        using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        await channel.QueueDeclareAsync(
            queue: MessagingConstants.OrdersQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        await _rabbitContainer.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.UseSetting("RabbitMQ:ConnectionString", _rabbitContainer.GetConnectionString());

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
    }
}

public class ApiIntegrationTests(TradePlatformTestFactory factory) : IClassFixture<TradePlatformTestFactory>
{
    private readonly TradePlatformTestFactory _factory = factory;

    [Fact]
    public async Task CreateTransaction_Should_Return_Accepted()
    {
        var userId = Guid.NewGuid().ToString();
        var sourceAccId = $"ACC_{Guid.NewGuid()}";
        var targetAccId = $"ACC_{Guid.NewGuid()}";

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TradeContext>();

            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var user = new ApplicationUser
            {
                Id = userId,
                UserName = $"IntegrationTestUser_{Guid.NewGuid()}",
                Email = $"test_{Guid.NewGuid()}@example.com",
                FullName = "Test User"
            };
            context.Users.Add(user);

            var acc1 = new Account
            {
                Id = sourceAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            var acc2 = new Account
            {
                Id = targetAccId,
                OwnerId = userId,
                Currency = Currency.FromCode("USD")
            };

            context.Accounts.AddRange(acc1, acc2);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");

        var request = new TransactionDto
        {
            SourceAccountId = sourceAccId,
            TargetAccountId = targetAccId,
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
        var client = _factory.CreateClient();

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