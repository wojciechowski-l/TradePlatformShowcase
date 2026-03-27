using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Entities;
using TradePlatform.Core.ValueObjects;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AccountsController(TradeContext context) : ControllerBase
    {
        [HttpGet("my-account")]
        public async Task<IActionResult> GetMyAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var account = await context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.OwnerId == userId);

            if (account == null)
            {
                return NotFound("No account found for user.");
            }

            return Ok(account);
        }

        [HttpGet("my-account/activity")]
        public async Task<IActionResult> GetMyAccountActivity([FromQuery] int take = 20)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var account = await context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.OwnerId == userId);

            if (account == null)
            {
                return NotFound("No account found for user.");
            }

            var items = await context.AccountActivityProjections
                .AsNoTracking()
                .Where(p => p.AccountId == account.Id)
                .OrderByDescending(p => p.CreatedAtUtc)
                .ThenByDescending(p => p.LastEventUtc)
                .Take(Math.Clamp(take, 1, 100))
                .Select(p => new AccountActivityDto
                {
                    TransactionId = p.TransactionId,
                    AccountId = p.AccountId,
                    CounterpartyAccountId = p.CounterpartyAccountId,
                    Direction = p.Direction,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    Status = p.Status,
                    CreatedAtUtc = p.CreatedAtUtc,
                    ProcessedAtUtc = p.ProcessedAtUtc,
                    LastEventUtc = p.LastEventUtc
                })
                .ToListAsync();

            return Ok(items);
        }

        [HttpPost("provision")]
        public async Task<IActionResult> ProvisionAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existing = await context.Accounts.FirstOrDefaultAsync(a => a.OwnerId == userId);
            if (existing != null) return Ok(existing);

            var newAccount = new Account
            {
                OwnerId = userId!,
                Currency = Currency.FromCode("USD")
            };

            context.Accounts.Add(newAccount);
            await context.SaveChangesAsync();

            return Ok(newAccount);
        }
    }
}
