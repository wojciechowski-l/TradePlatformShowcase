using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Api.Hubs
{
    [Authorize]
    public class TradeHub(IAccountOwnershipService ownershipService) : Hub
    {
        public async Task JoinAccountGroup(string accountId)
        {
            await ValidateAndExecuteAsync(accountId, async () =>
                await Groups.AddToGroupAsync(Context.ConnectionId, accountId));
        }

        public async Task LeaveAccountGroup(string accountId)
        {
            await ValidateAndExecuteAsync(accountId, async () =>
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, accountId));
        }

        private async Task ValidateAndExecuteAsync(string accountId, Func<Task> action)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return;

            var userId = (Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value) ?? throw new HubException("Unauthorized: User ID not found.");

            if (!await ownershipService.IsOwnerAsync(userId, accountId, Context.ConnectionAborted))
            {
                throw new HubException($"Unauthorized: User {userId} does not own account {accountId}.");
            }

            await action();
        }
    }
}