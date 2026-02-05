using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Worker.Consumers;

namespace TradePlatform.Tests.Worker
{
    public class TransactionConsumerTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        public async Task InitializeAsync() => await _dbContainer.StartAsync();
        public async Task DisposeAsync() => await _dbContainer.DisposeAsync();

        [Fact]
        public async Task Consume_Should_Process_Transaction_And_Publish_Update()
        {
            var services = new ServiceCollection();

            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;
            services.AddScoped(_ => new TradeContext(options));

            services.AddLogging(l => l.AddConsole());

            services.AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TransactionCreatedConsumer>();
            });

            var provider = services.BuildServiceProvider();
            var harness = provider.GetRequiredService<ITestHarness>();

            var transactionId = Guid.NewGuid();
            var accountId = "ACC_123";
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TradeContext>();
                await db.Database.EnsureCreatedAsync();
                db.Transactions.Add(new TransactionRecord
                {
                    Id = transactionId,
                    SourceAccountId = accountId,
                    TargetAccountId = "ACC_456",
                    Amount = 100,
                    Currency = "USD",
                    Status = TransactionStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await harness.Start();

            try
            {
                var evt = new TransactionCreatedEvent(transactionId, accountId, "ACC_456", 100, "USD");

                await harness.Bus.Publish(evt);

                Assert.True(await harness.Consumed.Any<TransactionCreatedEvent>(), "Message was not consumed.");

                // Verify DB was updated
                using (var scope = provider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<TradeContext>();
                    var tx = await db.Transactions.FindAsync(transactionId);
                    Assert.NotNull(tx);
                    Assert.Equal(TransactionStatus.Processed, tx.Status);
                }
                Assert.True(await harness.Published.Any<TransactionUpdateDto>(), "Notification update was not published.");
            }
            finally
            {
                await harness.Stop();
            }
        }
    }
}