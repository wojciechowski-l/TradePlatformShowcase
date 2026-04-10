using FluentValidation;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Config.Outbox;
using Rebus.OpenTelemetry.Configuration;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using TradePlatform.Api.Components;
using TradePlatform.Api.Endpoints;
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
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("TradePlatform");
    });

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("TradePlatform.Transactions")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddRebusInstrumentation()
        .AddSource("Rebus"));

builder.Services.AddScoped<IAccountOwnershipService, DbAccountOwnershipService>();
builder.Services.AddScoped<IAccountActivityProjectionRebuilder, AccountActivityProjectionRebuilder>();
builder.Services.AddScoped<IMessageInbox, SqlMessageInbox>();
builder.Services.AddScoped<IMessageMetadataAccessor, RebusMessageMetadataAccessor>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransactionScopeManager, RebusSqlTransactionScopeManager>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDbContextFactory<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ITradeContext>(provider => provider.GetRequiredService<TradeContext>());

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<TradeContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<TradeUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontends", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
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

if (args.Contains("--migrate-only"))
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting database migration...");
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeContext>();
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migration completed successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration failed.");
        Environment.Exit(1);
    }

    return;
}

using (var scope = app.Services.CreateScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<IBus>();
    await bus.Subscribe<TransactionStatusChangedEvent>();
    await bus.Subscribe<TransactionSubmittedEvent>();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowFrontends");
app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();

app.MapTradeAuthEndpoints();
app.MapControllers();
app.MapHub<TradeHub>("/hubs/trade");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
