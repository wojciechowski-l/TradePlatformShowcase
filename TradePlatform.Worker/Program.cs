using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TradePlatform.Core.Constants;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;
using TradePlatform.Worker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(seqUrl)
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TransactionCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ReceiveEndpoint(MessagingConstants.OrdersQueue, e =>
        {
            e.ConfigureConsumer<TransactionCreatedConsumer>(context);

            e.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(1)));
        });
    });
});

builder.Services.AddHostedService<OutboxPublisherWorker>();

try
{
    var host = builder.Build();
    Log.Information("Worker starting up with MassTransit...");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}