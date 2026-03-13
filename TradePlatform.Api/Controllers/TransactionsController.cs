using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TransactionsController(
        ITransactionService transactionService,
        IAccountOwnershipService accountOwnershipService) : ControllerBase
    {
        private readonly ITransactionService _transactionService = transactionService;
        private readonly IAccountOwnershipService _accountOwnershipService = accountOwnershipService;

        [HttpPost]
        public async Task<IActionResult> CreateTransaction(
            [FromBody] TransactionDto request,
            CancellationToken cancellationToken)
        {
            if (!await _accountOwnershipService.IsOwnerAsync(User, request.SourceAccountId, cancellationToken))
            {
                return Forbid();
            }

            var result = await _transactionService.CreateTransactionAsync(request, cancellationToken);

            return Accepted(new { id = result.TransactionId, status = result.Status });
        }
    }
}