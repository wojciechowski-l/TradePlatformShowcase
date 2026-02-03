using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TradePlatform.Api;
using TradePlatform.Api.Hubs;
using TradePlatform.Api.Infrastructure;
using TradePlatform.Core.Entities;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Data;
using TradePlatform.Infrastructure.Messaging;
using TradePlatform.Infrastructure.Services;
using FluentValidation;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .WriteTo.Console()
    .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://seq"));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();

var rabbitHost = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
builder.Services.AddSingleton<IRabbitMQConnection>(sp => new RabbitMQConnection(rabbitHost));
builder.Services.AddScoped<IMessageProducer, RabbitMQProducer>();

builder.Services.AddScoped<IAccountOwnershipService, DbAccountOwnershipService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();

builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<TradeContext>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TradeContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ITradeContext>(provider => provider.GetRequiredService<TradeContext>());

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

builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<NotificationWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<TradeContext>();
        if (context.Database.GetPendingMigrations().Any())
        {
            context.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors("AllowFrontends");
app.MapHealthChecks("/health");

app.Use(async (context, next) =>
{
    var accessToken = context.Request.Query["access_token"];

    if (!string.IsNullOrEmpty(accessToken) &&
        context.Request.Path.StartsWithSegments("/hubs"))
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Request.Headers.Authorization = "Bearer " + accessToken;
        }
    }

    await next();
});


app.UseAuthentication();
app.UseAuthorization();
app.MapGroup("/api/auth").MapIdentityApi<ApplicationUser>();
app.MapControllers();
app.MapHub<TradeHub>("/hubs/trade");

app.Run();