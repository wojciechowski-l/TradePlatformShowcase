using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using TradePlatform.Core.Entities;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;
using TradePlatform.Worker;
using TradePlatform.Core.Constants;
using Xunit;

namespace TradePlatform.Tests.Worker
{
    public class WorkerTests : IAsyncLifetime
    {
        private readonly MsSqlContainer _dbContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        private readonly RabbitMqContainer _mqContainer = new RabbitMqBuilder("rabbitmq:3.13-management").Build();

        // Polish: Centralized Test Configuration
        private const int MaxPolls = 20;
        private const int PollDelayMs = 500;

        public async Task InitializeAsync()
        {
            await Task.WhenAll(_dbContainer.StartAsync(), _mqContainer.StartAsync());

            using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();

            var mqConnection = new RabbitMQConnection(_mqContainer.GetConnectionString());
            await RabbitMQTopologySetup.InitializeAsync(mqConnection);
        }

        public async Task DisposeAsync()
        {
            await _dbContainer.DisposeAsync();
            await _mqContainer.DisposeAsync();
        }

        private TradeContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(_dbContainer.GetConnectionString())
                .Options;
            return new TradeContext(options);
        }

        // Polish: Centralized Connection Factory Logic
        private ConnectionFactory CreateTestConnectionFactory()
        {
            return new ConnectionFactory { Uri = new Uri(_mqContainer.GetConnectionString()) };
        }

        [Fact]
        public async Task Worker_Should_ConsumeMessage_And_MarkTransactionAsProcessed()
        {
            await RunWorkerTestAsync(async (channel, transactionId) =>
            {
                await PublishMessageAsync(channel, transactionId);
            });
        }

        [Fact]
        public async Task Worker_Should_Handle_Duplicate_Messages_Gracefully()
        {
            await RunWorkerTestAsync(async (channel, transactionId) =>
            {
                // 1. Publish the SAME message TWICE
                await PublishMessageAsync(channel, transactionId);
                await PublishMessageAsync(channel, transactionId);
            });
        }

        [Fact]
        public async Task Worker_Should_Reject_Malformatted_Message_To_DLQ()
        {
            // 1. Publish "Poison" message (Invalid JSON)
            var poisonMessage = "This is not a GUID";

            // Polish: Use helper
            var factory = CreateTestConnectionFactory();
            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            var body = Encoding.UTF8.GetBytes(poisonMessage);
            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: MessagingConstants.OrdersQueue,
                mandatory: false,
                body: body);

            // 2. Start Worker
            var services = new ServiceCollection();
            services.AddDbContext<TradeContext>(opts => opts.UseSqlServer(_dbContainer.GetConnectionString()));
            services.AddSingleton<IRabbitMQConnection>(new RabbitMQConnection(_mqContainer.GetConnectionString()));

            var sp = services.BuildServiceProvider();

            try
            {
                var worker = await StartWorkerAsync(sp);

                // 3. Poll DLQ for the rejected message
                BasicGetResult? result = null;

                // Polish: Use constants
                for (int i = 0; i < MaxPolls; i++)
                {
                    await Task.Delay(PollDelayMs);
                    result = await channel.BasicGetAsync(MessagingConstants.OrdersDeadLetterQueue, autoAck: true);
                    if (result != null) break;
                }

                await worker.StopAsync(CancellationToken.None);

                // 4. Assert
                Assert.NotNull(result);
                var deadBody = Encoding.UTF8.GetString(result.Body.ToArray());
                Assert.Equal(poisonMessage, deadBody);
            }
            finally
            {
                (sp as IDisposable)?.Dispose();
            }
        }

        private async Task RunWorkerTestAsync(Func<IChannel, Guid, Task> publishAction)
        {
            var transactionId = Guid.NewGuid();

            // 1. Seed
            using (var seedContext = CreateContext())
            {
                seedContext.Transactions.Add(new TransactionRecord
                {
                    Id = transactionId,
                    Status = TransactionStatus.Pending,
                    SourceAccountId = "A",
                    TargetAccountId = "B",
                    Amount = 100,
                    Currency = "USD",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await seedContext.SaveChangesAsync();
            }

            // 2. Publish
            // Polish: Use helper
            var factory = CreateTestConnectionFactory();
            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            await publishAction(channel, transactionId);

            // 3. Start Worker
            var services = new ServiceCollection();
            services.AddDbContext<TradeContext>(opts => opts.UseSqlServer(_dbContainer.GetConnectionString()));
            services.AddSingleton<IRabbitMQConnection>(new RabbitMQConnection(_mqContainer.GetConnectionString()));

            var sp = services.BuildServiceProvider();

            try
            {
                var worker = await StartWorkerAsync(sp);

                // 4. Assert with Polling
                bool processed = false;

                // Polish: Use constants
                for (int i = 0; i < MaxPolls; i++)
                {
                    await Task.Delay(PollDelayMs);
                    using var assertContext = CreateContext();
                    var tx = await assertContext.Transactions.FindAsync(transactionId);

                    if (tx!.Status == TransactionStatus.Processed)
                    {
                        processed = true;
                        break;
                    }
                }

                await worker.StopAsync(CancellationToken.None);
                Assert.True(processed, "Transaction was not processed correctly.");
            }
            finally
            {
                (sp as IDisposable)?.Dispose();
            }
        }

        private static async Task<TradePlatform.Worker.Worker> StartWorkerAsync(IServiceProvider sp)
        {
            var worker = new TradePlatform.Worker.Worker(
                NullLogger<TradePlatform.Worker.Worker>.Instance,
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IRabbitMQConnection>()
            );

            await worker.StartAsync(CancellationToken.None);
            return worker;
        }

        private static async Task PublishMessageAsync(IChannel channel, Guid id)
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(id));
            await channel.BasicPublishAsync(
                exchange: "",
                routingKey: MessagingConstants.OrdersQueue,
                mandatory: false,
                body: body);
        }
    }
}