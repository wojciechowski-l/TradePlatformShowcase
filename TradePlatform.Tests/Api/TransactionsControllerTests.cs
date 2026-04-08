using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;
using TradePlatform.Api.Controllers;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Tests.Api;

public class TransactionsControllerTests
{
    [Fact]
    public async Task CreateTransaction_Should_Return_Forbid_When_User_Does_Not_Own_Source_Account()
    {
        var transactionService = new Mock<ITransactionService>(MockBehavior.Strict);
        var ownershipService = new Mock<IAccountOwnershipService>();
        ownershipService
            .Setup(service => service.IsOwnerAsync(It.IsAny<ClaimsPrincipal>(), "ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = CreateController(transactionService.Object, ownershipService.Object, CreatePrincipal("user-1"));

        var result = await controller.CreateTransaction(
            new TransactionDto
            {
                SourceAccountId = "ACC-001",
                TargetAccountId = "ACC-002",
                Amount = 10m,
                Currency = "USD"
            },
            null,
            TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
        transactionService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateTransaction_Should_Return_Accepted_When_User_Owns_Source_Account()
    {
        var transactionService = new Mock<ITransactionService>();
        var ownershipService = new Mock<IAccountOwnershipService>();
        ownershipService
            .Setup(service => service.IsOwnerAsync(It.IsAny<ClaimsPrincipal>(), "ACC-100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        transactionService
            .Setup(service => service.CreateTransactionAsync(
                It.IsAny<TransactionDto>(),
                "idem-1",
                "user-100",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTransactionResult
            {
                TransactionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Status = TransactionStatus.Pending
            });

        var controller = CreateController(transactionService.Object, ownershipService.Object, CreatePrincipal("user-100"));

        var result = await controller.CreateTransaction(
            new TransactionDto
            {
                SourceAccountId = "ACC-100",
                TargetAccountId = "ACC-200",
                Amount = 25m,
                Currency = "USD"
            },
            "idem-1",
            TestContext.Current.CancellationToken);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(accepted.Value);
        transactionService.VerifyAll();
    }

    private static TransactionsController CreateController(
        ITransactionService transactionService,
        IAccountOwnershipService ownershipService,
        ClaimsPrincipal user)
    {
        return new TransactionsController(transactionService, ownershipService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            }
        };
    }

    private static ClaimsPrincipal CreatePrincipal(string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "Test"));
    }
}
