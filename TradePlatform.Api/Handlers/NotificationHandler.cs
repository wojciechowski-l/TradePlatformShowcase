using Microsoft.AspNetCore.SignalR;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.DTOs;
using Wolverine;

namespace TradePlatform.Api.Handlers;

public partial class NotificationHandler(IHubContext<TradeHub> hubContext, ILogger<NotificationHandler> logger)
{
    public async Task Handle(TransactionUpdateDto notification)
    {
        if (!string.IsNullOrEmpty(notification.AccountId))
        {
            await hubContext.Clients.Group(notification.AccountId)
                .SendAsync("ReceiveStatusUpdate", notification);

            LogProcessing(logger, notification.TransactionId, notification.AccountId);               
        }
    }

    [LoggerMessage(LogLevel.Information, "Pushed SignalR update for Tx {TransactionId} to Account {AccountId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId, string accountId);
}