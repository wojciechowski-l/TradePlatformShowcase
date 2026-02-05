using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;
using TradePlatform.Worker;

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
builder.Services.AddSingleton<IRabbitMQConnection>(
    _ => new RabbitMQConnection(rabbitHost));

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddHostedService<Worker>();
try
{
    var host = builder.Build();

    var mqConnection = host.Services.GetRequiredService<IRabbitMQConnection>();
    await RabbitMQTopologySetup.InitializeAsync(mqConnection);
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