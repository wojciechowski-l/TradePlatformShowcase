using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using TradePlatform.BlazorClient.Models;

namespace TradePlatform.BlazorClient.Services;

public sealed class TradeSignalRService(NavigationManager navigationManager) : IAsyncDisposable
{
    private HubConnection? _connection;
    private string? _joinedAccountId;

    public event Action<TransactionUpdate>? StatusUpdated;

    public async Task ConnectAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return;
        }

        _connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/trade"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<TransactionUpdate>("ReceiveStatusUpdate", update => StatusUpdated?.Invoke(update));
        _connection.Reconnected += async _ =>
        {
            if (!string.IsNullOrWhiteSpace(_joinedAccountId))
            {
                await JoinAccountGroupAsync(_joinedAccountId, cancellationToken);
            }
        };

        await _connection.StartAsync(cancellationToken);
    }

    public async Task JoinAccountGroupAsync(string accountId, CancellationToken cancellationToken = default)
    {
        _joinedAccountId = accountId;

        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("JoinAccountGroup", accountId, cancellationToken);
        }
    }

    public async Task DisconnectAsync()
    {
        _joinedAccountId = null;

        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
