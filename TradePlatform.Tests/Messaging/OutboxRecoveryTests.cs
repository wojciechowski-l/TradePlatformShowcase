using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.MsSql;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;
using System.Text.Json;

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
                    Payload = JsonSerializer.Serialize(new TransactionCreatedEvent(Guid.NewGuid(), "A", "B", 10, "USD")),
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    Status = OutboxStatus.InFlight,
                    LastAttemptAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    AttemptCount = 1
                };
                seedContext.OutboxMessages.Add(stuckMessage);
                await seedContext.SaveChangesAsync();
            }

            var services = new ServiceCollection();
            services.AddScoped(_ => CreateContext());
            var sp = services.BuildServiceProvider();

            var mockPublishEndpoint = new Mock<IPublishEndpoint>();
            mockPublishEndpoint
                .Setup(x => x.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var configParams = new Dictionary<string, string> {
                {"Outbox:PollIntervalSeconds", "1"},
                {"Outbox:StuckThresholdMinutes", "5"}
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configParams!).Build();

            var worker = new OutboxPublisherWorker(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<OutboxPublisherWorker>.Instance,
                mockPublishEndpoint.Object,
                configuration
            );

            await worker.StartAsync(CancellationToken.None);

            bool rescuedAndProcessed = false;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                using var checkContext = CreateContext();
                var msg = await checkContext.OutboxMessages.SingleAsync();

                if (msg.Status == OutboxStatus.Processed)
                {
                    rescuedAndProcessed = true;
                    break;
                }
            }

            await worker.StopAsync(CancellationToken.None);

            Assert.True(rescuedAndProcessed, "The Sweeper failed to rescue and process the stuck message within the timeout.");

            mockPublishEndpoint.Verify(x => x.Publish(
                It.IsAny<TransactionCreatedEvent>(),
                It.IsAny<IPipe<PublishContext<TransactionCreatedEvent>>>(),
                It.IsAny<CancellationToken>()
            ), Times.AtLeastOnce);
        }
    }
}