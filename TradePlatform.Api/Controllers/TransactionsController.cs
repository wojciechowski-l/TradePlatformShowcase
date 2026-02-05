using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TransactionsController(ITransactionService transactionService) : ControllerBase
    {
        private readonly ITransactionService _transactionService = transactionService;

        [HttpPost]
        public async Task<IActionResult> CreateTransaction([FromBody] TransactionDto request)
        {
            var result = await _transactionService.CreateTransactionAsync(request);

            return Accepted(result);
        }
    }
}