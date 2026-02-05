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
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using MassTransit;
using TradePlatform.Core.Constants;

namespace TradePlatform.Tests.Integration
{
    public class ApiIntegrationTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        public async Task InitializeAsync()
        {
            await _dbContainer.StartAsync();

            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;

            using var context = new TradeContext(options);
            await context.Database.MigrateAsync();

            // 1. Seed Users first (Required for Foreign Keys)
            if (!await context.Users.AnyAsync())
            {
                context.Users.AddRange(
                    new ApplicationUser { Id = "User_A", UserName = "User_A", Email = "a@test.com" },
                    new ApplicationUser { Id = "User_B", UserName = "User_B", Email = "b@test.com" }
                );
                await context.SaveChangesAsync();
            }

            // 2. Seed Accounts with correct property names
            if (!await context.Accounts.AnyAsync())
            {
                context.Accounts.AddRange(
                    new Account { Id = "ACC_123", OwnerId = "User_A", Currency = "USD" },
                    new Account { Id = "ACC_456", OwnerId = "User_B", Currency = "USD" }
                );
                await context.SaveChangesAsync();
            }
        }

        public async Task DisposeAsync() => await _dbContainer.DisposeAsync();

        [Fact]
        public async Task CreateTransaction_Should_Return_Created_And_Persist_To_Db()
        {
            // Arrange
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        // FIX: Use generic RemoveAll<T> to resolve CA2263
                        services.RemoveAll<DbContextOptions<TradeContext>>();
                        services.RemoveAll<TradeContext>();

                        services.AddDbContext<TradeContext>(options =>
                        {
                            options.UseSqlServer(_dbContainer.GetConnectionString());
                        });

                        services.AddAuthentication(defaultScheme: "TestScheme")
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });

                        services.AddMassTransitTestHarness();
                    });
                });

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");

            var request = new TransactionDto
            {
                SourceAccountId = "ACC_123",
                TargetAccountId = "ACC_456",
                Amount = 100.50m,
                Currency = "USD"
            };

            // Act
            var response = await client.PostAsJsonAsync("/api/transactions", request);

            // Assert API Response
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<CreateTransactionResult>();

            Assert.NotNull(result);
            Assert.NotEqual(Guid.Empty, result.TransactionId);
            Assert.Equal(TransactionStatus.Pending, result.Status);

            // Assert DB Persistence
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TradeContext>();

            var txInDb = await context.Transactions.FindAsync(result.TransactionId);

            Assert.NotNull(txInDb);
            Assert.Equal(100.50m, txInDb.Amount);
            Assert.Equal("ACC_123", txInDb.SourceAccountId);

            var outboxMsg = await context.OutboxMessages.FirstOrDefaultAsync();
            Assert.NotNull(outboxMsg);
            Assert.Equal("TransactionCreated", outboxMsg.Type);
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
}