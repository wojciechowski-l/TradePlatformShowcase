using Microsoft.EntityFrameworkCore;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Serilog;
using Serilog.Events;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Worker.Handlers;

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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddRebus(configure =>
{
    var rabbitUri = builder.Configuration["RabbitMQ:ConnectionString"]
        ?? $"amqp://guest:guest@{builder.Configuration["RabbitMQ:Host"] ?? "localhost"}:5672";

    return configure
        .Logging(l => l.Serilog())
        .Transport(t => t.UseRabbitMq(rabbitUri, MessagingConstants.OrdersQueue))
        .Routing(r => r.TypeBased().Map<TransactionUpdateDto>(MessagingConstants.NotificationsQueue))
        .Options(o =>
        {
            o.SetNumberOfWorkers(5);
            o.RetryStrategy(maxDeliveryAttempts: 3);
        });
});

builder.Services.AutoRegisterHandlersFromAssemblyOf<TransactionCreatedHandler>();

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