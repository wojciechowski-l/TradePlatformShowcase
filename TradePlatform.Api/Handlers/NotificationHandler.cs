using Microsoft.AspNetCore.SignalR;
using Rebus.Handlers;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.DTOs;

namespace TradePlatform.Api.Handlers;

public partial class NotificationHandler(IHubContext<TradeHub> hubContext, ILogger<NotificationHandler> logger)
    : IHandleMessages<TransactionProcessedEvent>
{
    public async Task Handle(TransactionProcessedEvent message)
    {
        if (!string.IsNullOrEmpty(message.AccountId))
        {
            var dto = new TransactionUpdateDto
            {
                TransactionId = message.TransactionId,
                Status = message.Status,
                AccountId = message.AccountId,
                UpdatedAtUtc = message.ProcessedAtUtc
            };

            await hubContext.Clients.Group(message.AccountId)
                .SendAsync("ReceiveStatusUpdate", dto);

            LogProcessing(logger, message.TransactionId, message.AccountId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Pushed SignalR update for Tx {TransactionId} to Account {AccountId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId, string accountId);
}