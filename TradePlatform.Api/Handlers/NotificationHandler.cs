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
        var accountIds = new[] { message.SourceAccountId, message.TargetAccountId }
            .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
            .Distinct()
            .ToArray();

        foreach (var accountId in accountIds)
        {
            var dto = new TransactionUpdateDto
            {
                TransactionId = message.TransactionId,
                Status = message.Status,
                AccountId = accountId,
                UpdatedAtUtc = message.ProcessedAtUtc
            };

            await hubContext.Clients.Group(accountId)
                .SendAsync("ReceiveStatusUpdate", dto);
        }

        foreach (var accountId in accountIds)
        {
            LogProcessing(logger, message.TransactionId, accountId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Pushed SignalR update for Tx {TransactionId} to Account {AccountId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId, string accountId);
}
