using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TradePlatform.BlazorClient;
using TradePlatform.BlazorClient.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.HostEnvironment.BaseAddress.TrimEnd('/');
}

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ClientAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ClientAuthenticationStateProvider>());
builder.Services.AddScoped<IAuthTokenAccessor>(sp => sp.GetRequiredService<ClientAuthenticationStateProvider>());
builder.Services.AddScoped<AuthTokenHandler>();
builder.Services.AddHttpClient<ApiClient>(client =>
    {
        client.BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/");
    })
    .AddHttpMessageHandler<AuthTokenHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.UseJitter = true;
    });
builder.Services.AddScoped<TradeSignalRService>();

await builder.Build().RunAsync();
