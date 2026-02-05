using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Testcontainers.MsSql;
using TradePlatform.Api;
using TradePlatform.Api.Infrastructure;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;

namespace TradePlatform.Tests.Integration
{
    public class ApiIntegrationTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        public async Task InitializeAsync() => await _dbContainer.StartAsync();
        public async Task DisposeAsync() => await _dbContainer.DisposeAsync();

        private static void RemoveBackgroundServices(IServiceCollection services)
        {
            var workerTypes = new[]
            {
                typeof(OutboxPublisherWorker),
                typeof(NotificationWorker),
                typeof(InfrastructureInitializer)
            };

            var descriptors = services.Where(d =>
                d.ImplementationType != null && workerTypes.Contains(d.ImplementationType)
            ).ToList();

            foreach (var d in descriptors)
            {
                services.Remove(d);
            }
        }

        [Fact]
        public async Task CreateTransaction_Should_PersistTo_RealSql_Atomically()
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<DbContextOptions<TradeContext>>();
                        services.AddDbContext<TradeContext>(options =>
                            options.UseSqlServer(_dbContainer.GetConnectionString()));

                        RemoveBackgroundServices(services);

                        services.AddAuthentication(defaultScheme: "TestScheme")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                                "TestScheme", options => { });
                    });
                });

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TradeContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");

            var request = new TransactionDto
            {
                SourceAccountId = "RealSQL_A",
                TargetAccountId = "RealSQL_B",
                Amount = 500,
                Currency = "EUR"
            };

            var response = await client.PostAsJsonAsync("/api/transactions", request);

            Assert.True(response.IsSuccessStatusCode, $"Expected success but got {response.StatusCode}");

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TradeContext>();
                var tx = await db.Transactions.FirstOrDefaultAsync(t => t.SourceAccountId == "RealSQL_A");
                Assert.NotNull(tx);

                var outbox = await db.OutboxMessages.FirstOrDefaultAsync();
                Assert.NotNull(outbox);
                Assert.Contains(tx.Id.ToString(), outbox.Payload);
            }
        }

        [Fact]
        public async Task CreateTransaction_Should_Return400_When_Validation_Fails()
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<DbContextOptions<TradeContext>>();
                        var dbConfig = services.FirstOrDefault(d => d.ServiceType.Name == "IDbContextOptionsConfiguration`1");
                        if (dbConfig != null) services.Remove(dbConfig);
                        services.AddDbContext<TradeContext>(options =>
                            options.UseInMemoryDatabase("Integration_Validation_Test"));
                        RemoveBackgroundServices(services);
                        services.AddAuthentication(defaultScheme: "TestScheme")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", o => { });
                    });
                });

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

            var response = await client.PostAsJsonAsync("/api/transactions", request);

            Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Source and Target accounts must be different", body);
        }
    }

    public class TestAuthHandler(
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
}