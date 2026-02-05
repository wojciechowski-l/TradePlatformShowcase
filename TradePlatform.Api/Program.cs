using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TradePlatform.Api;
using TradePlatform.Api.Hubs;
using TradePlatform.Api.Infrastructure;
using TradePlatform.Core.Constants;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Services;
using FluentValidation;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;
using ExchangeType = Wolverine.RabbitMQ.ExchangeType;

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

builder.Services.AddScoped<IAccountOwnershipService, DbAccountOwnershipService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();

builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<TradeContext>()
    .AddClaimsPrincipalFactory<TradeUserClaimsPrincipalFactory>();

builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// SIMPLIFIED WOLVERINE CONFIG
builder.Host.UseWolverine(opts =>
{
    // Standard EF Core Integration
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    // Transport Configuration (Skipped in Tests)
    if (!builder.Environment.IsEnvironment("Test"))
    {
        var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
        var connectionUri = new Uri($"amqp://guest:guest@{rabbitHost}:5672");

        opts.UseRabbitMq(connectionUri)
            .AutoProvision()
            .BindExchange(MessagingConstants.NotificationsExchange, ExchangeType.Fanout)
            .ToQueue(MessagingConstants.NotificationsQueue);

        opts.PublishMessage<TradePlatform.Core.DTOs.TransactionCreatedEvent>()
            .ToRabbitQueue(MessagingConstants.OrdersQueue);

        opts.PublishMessage<TradePlatform.Core.DTOs.TransactionUpdateDto>()
            .ToRabbitExchange(MessagingConstants.NotificationsExchange, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Fanout;
            });

        opts.ListenToRabbitQueue(MessagingConstants.NotificationsQueue);
    }
});
// Note: Removed ExtensionDiscovery.ManualOnly argument

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
app.MapHealthChecks("/health");
app.UseAuthentication();
app.UseAuthorization();
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapControllers();
app.MapHub<TradeHub>("/hubs/trade");

app.Run();