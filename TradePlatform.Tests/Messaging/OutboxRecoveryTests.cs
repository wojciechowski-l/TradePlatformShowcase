using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;

namespace TradePlatform.Tests.Messaging
{
    public class OutboxRecoveryTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        public async Task InitializeAsync()
        {
            await _dbContainer.StartAsync();
            using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
        }

        public async Task DisposeAsync()
        {
            await _dbContainer.DisposeAsync();
        }

        private TradeContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;
            return new TradeContext(options);
        }

        [Fact]
        public async Task Worker_Should_Rescue_Stuck_Messages_From_Crashed_Workers()
        {
            using (var seedContext = CreateContext())
            {
                var stuckMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = "TransactionCreated",
                    Payload = "{}",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
                    ProcessedAtUtc = DateTime.UtcNow.AddYears(-100).AddMinutes(-10),
                    AttemptCount = 0
                };
                seedContext.OutboxMessages.Add(stuckMessage);
                await seedContext.SaveChangesAsync();
            }

            var services = new ServiceCollection();

            var dbOptions = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;

            services.AddSingleton(dbOptions);
            services.AddScoped<TradeContext>();
            services.AddLogging();

            services.AddScoped(_ => new Moq.Mock<TradePlatform.Core.Interfaces.IMessageProducer>().Object);

            var sp = services.BuildServiceProvider();

            try
            {
                var configParams = new Dictionary<string, string> {
                    {"Outbox:PollIntervalSeconds", "1"},
                    {"Outbox:StuckThresholdMinutes", "5"}
                };
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(configParams!).Build();

                var worker = new OutboxPublisherWorker(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<OutboxPublisherWorker>.Instance,
                    configuration
                );

                await worker.StartAsync(CancellationToken.None);

                bool rescued = false;
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(500);
                    using var checkContext = CreateContext();
                    var msg = await checkContext.OutboxMessages.SingleAsync();

                    if (msg.ProcessedAtUtc == null)
                    {
                        rescued = true;
                        break;
                    }
                }

                await worker.StopAsync(CancellationToken.None);
                Assert.True(rescued, "The Sweeper failed to rescue the stuck message within the timeout.");
            }
            finally
            {
                (sp as IDisposable)?.Dispose();
            }
        }
    }
}