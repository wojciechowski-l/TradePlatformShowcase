using Microsoft.AspNetCore.SignalR;
using Rebus.Handlers;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Interfaces;
using TradePlatform.Infrastructure.Services;

namespace TradePlatform.Api.Handlers;

public partial class NotificationHandler(
    IHubContext<TradeHub> hubContext,
    IMessageMetadataAccessor messageMetadataAccessor,
    ILogger<NotificationHandler> logger)
    : IHandleMessages<TransactionStatusChangedEvent>
{
    public async Task Handle(TransactionStatusChangedEvent message)
    {
        var deliveryCount = messageMetadataAccessor.GetCurrentDeliveryCount();
        if (deliveryCount > 1)
        {
            MessagingMetrics.RecordRetryAttempt(typeof(NotificationHandler).FullName!, typeof(TransactionStatusChangedEvent).FullName!);
        }

        var eventId = messageMetadataAccessor.GetCurrentMessageId()
            ?? $"{message.TransactionId:N}:{message.CurrentStatus}:{message.ChangedAtUtc:O}";

        var accountIds = new[] { message.SourceAccountId, message.TargetAccountId }
            .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
            .Distinct()
            .ToArray();

        foreach (var accountId in accountIds)
        {
            var dto = new TransactionUpdateDto
            {
                EventId = eventId,
                TransactionId = message.TransactionId,
                Status = message.CurrentStatus,
                AccountId = accountId,
                UpdatedAtUtc = message.ChangedAtUtc,
                FailureReason = message.FailureReason
            };

            await hubContext.Clients.Group(accountId)
                .SendAsync("ReceiveStatusUpdate", dto);

            MessagingMetrics.RecordStatusUpdatePush(accountId, message.CurrentStatus.ToString());
        }

        foreach (var accountId in accountIds)
        {
            LogProcessing(logger, message.TransactionId, accountId);
        }
    }

    [LoggerMessage(LogLevel.Information, "Pushed SignalR update for Tx {TransactionId} to Account {AccountId}")]
    static partial void LogProcessing(ILogger logger, Guid transactionId, string accountId);
}
