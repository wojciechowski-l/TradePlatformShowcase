using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            if (!await _accountOwnershipService.IsOwnerAsync(User, request.SourceAccountId, cancellationToken))
            {
                return Forbid();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

            try
            {
                var result = await _transactionService.CreateTransactionAsync(
                    request, idempotencyKey, userId, cancellationToken);

                return Accepted(new { id = result.TransactionId, status = result.Status });
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                return Conflict("A transaction with this idempotency key is already being processed.");
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
        }
    }
}