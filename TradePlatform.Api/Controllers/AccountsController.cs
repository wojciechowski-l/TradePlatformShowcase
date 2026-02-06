using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

        [HttpPost("provision")]
        public async Task<IActionResult> ProvisionAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var existing = await context.Accounts.FirstOrDefaultAsync(a => a.OwnerId == userId);
            if (existing != null) return Ok(existing);

            var newAccount = new Account
            {
                Id = $"ACC-{new Random().Next(10000, 99999)}",
                OwnerId = userId!,
                Currency = Currency.FromCode("USD"),
            };

            context.Accounts.Add(newAccount);
            await context.SaveChangesAsync();

            return Ok(newAccount);
        }
    }
}