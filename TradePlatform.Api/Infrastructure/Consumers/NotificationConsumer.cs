using MassTransit;
using Microsoft.AspNetCore.SignalR;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.DTOs;

namespace TradePlatform.Api.Infrastructure.Consumers
{
    public partial class NotificationConsumer(
        IHubContext<TradeHub> hubContext,
        ILogger<NotificationConsumer> logger
    ) : IConsumer<TransactionUpdateDto>
    {
        private readonly ILogger<NotificationConsumer> _logger = logger;

        public async Task Consume(ConsumeContext<TransactionUpdateDto> context)
        {
            var update = context.Message;

            if (!string.IsNullOrEmpty(update.AccountId))
            {
                // Push to the specific user's SignalR group
                await hubContext.Clients.Group(update.AccountId)
                    .SendAsync("ReceiveStatusUpdate", update, cancellationToken: context.CancellationToken);

                LogUpdatePushed(update.TransactionId, update.AccountId);
            }
        }

        [LoggerMessage(LogLevel.Information, "Pushed update for Tx {Id} to Account {Acc}")]
        private partial void LogUpdatePushed(Guid id, string acc);
    }
}