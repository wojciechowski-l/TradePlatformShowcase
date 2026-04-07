using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TradePlatform.BlazorClient.Models;
using TradePlatform.BlazorClient.Services;
using TradePlatform.BlazorClient;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    apiBaseUrl = builder.HostEnvironment.BaseAddress.TrimEnd('/');
}

builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri($"{apiBaseUrl.TrimEnd('/')}/")
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<TradeSignalRService>();

await builder.Build().RunAsync();
