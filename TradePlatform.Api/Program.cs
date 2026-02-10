using FluentValidation;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Rebus.Config;
using Rebus.Config.Outbox;
using Rebus.OpenTelemetry.Configuration;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using TradePlatform.Api.Handlers;
using TradePlatform.Api.Hubs;
using TradePlatform.Api.Infrastructure;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .WriteTo.Console();

    if (!builder.Environment.IsEnvironment("Test"))
    {
        configuration.WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://seq");
    }
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddRebusInstrumentation()
        .AddSource("Rebus"));

builder.Services.AddScoped<IAccountOwnershipService, DbAccountOwnershipService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();

builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<TradeContext>()
    .AddClaimsPrincipalFactory<TradeUserClaimsPrincipalFactory>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ITradeContext>(p => p.GetRequiredService<TradeContext>());

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontends", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddRebus(configure =>
{
    var rabbitUri = builder.Configuration["RabbitMQ:ConnectionString"]
        ?? $"amqp://guest:guest@{builder.Configuration["RabbitMQ:Host"] ?? "localhost"}:5672";

    return configure
        .Logging(l => l.Serilog())
        .Transport(t => t.UseRabbitMq(rabbitUri, MessagingConstants.NotificationsQueue))
        .Outbox(o => o.StoreInSqlServer(connectionString, "RebusOutbox"))
        .Routing(r => r.TypeBased().Map<TransactionCreatedEvent>(MessagingConstants.OrdersQueue))
        .Options(o =>
        {
            o.SetNumberOfWorkers(1);
            o.RetryStrategy(maxDeliveryAttempts: 3);
            o.EnableDiagnosticSources();
        });
});

builder.Services.AutoRegisterHandlersFromAssemblyOf<NotificationHandler>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    try
    {
        scope.ServiceProvider.GetRequiredService<TradeContext>().Database.Migrate();
    }
    catch { }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowFrontends");
app.UseSerilogRequestLogging();
app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();
app.UseAuthentication();
app.UseAuthorization();
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapControllers();
app.MapHub<TradeHub>("/hubs/trade");

app.Run();