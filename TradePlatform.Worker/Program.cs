using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

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

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

// SIMPLIFIED WOLVERINE CONFIG
builder.Services.AddWolverine(opts =>
{
    // Persistence (Required for DurableInbox)
    opts.PersistMessagesWithSqlServer(connectionString, "wolverine");

    // Transport
    opts.UseRabbitMq(new Uri($"amqp://guest:guest@{rabbitHost}:5672"))
        .AutoProvision();

    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();

    opts.ListenToRabbitQueue(MessagingConstants.OrdersQueue)
        .UseDurableInbox();

    opts.Publish(rules =>
    {
        rules.MessagesFromNamespace("TradePlatform.Core.DTOs")
             .ToRabbitExchange(MessagingConstants.NotificationsExchange, exchange =>
             {
                 exchange.ExchangeType = ExchangeType.Fanout;
                 // Optional: Durable to ensure SignalR gets it even if restart happens
                 exchange.IsDurable = true;
             });
    });
    // Removed: DisableConventionalDiscovery()
    // Removed: Manual IncludeType<Handler>()
});

try
{
    var host = builder.Build();
    Log.Information("Worker starting up...");
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