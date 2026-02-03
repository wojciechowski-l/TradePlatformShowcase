using TradePlatform.Infrastructure.Messaging;

namespace TradePlatform.Api;

public class InfrastructureInitializer(
    IServiceProvider serviceProvider,
    ILogger<InfrastructureInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Infrastructure Initializer started. Connecting to RabbitMQ...");

        try
        {
            using var scope = serviceProvider.CreateScope();
            var mqConnection = scope.ServiceProvider.GetRequiredService<IRabbitMQConnection>();

            await RabbitMQTopologySetup.InitializeAsync(mqConnection);

            logger.LogInformation("RabbitMQ Topology initialized successfully!");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to initialize RabbitMQ Topology. The worker/API might not receive messages.");
        }
    }
}