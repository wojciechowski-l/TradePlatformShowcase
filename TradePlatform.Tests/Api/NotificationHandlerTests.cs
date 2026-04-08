using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using TradePlatform.Api.Handlers;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.Constants;
using TradePlatform.Core.DTOs;

namespace TradePlatform.Tests.Api;

public class NotificationHandlerTests
{
    [Fact]
    public async Task Handle_Should_Send_Update_To_Distinct_Source_And_Target_Groups()
    {
        var sourceProxy = new Mock<IClientProxy>();
        var targetProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group("ACC-001")).Returns(sourceProxy.Object);
        clients.Setup(c => c.Group("ACC-002")).Returns(targetProxy.Object);

        var hubContext = new Mock<IHubContext<TradeHub>>();
        hubContext.SetupGet(context => context.Clients).Returns(clients.Object);

        var handler = new NotificationHandler(hubContext.Object, Mock.Of<ILogger<NotificationHandler>>());
        var message = new TransactionStatusChangedEvent(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "ACC-001",
            "ACC-002",
            75m,
            "USD",
            TransactionStatus.Processing,
            TransactionStatus.Failed,
            DateTime.UtcNow,
            "Source account has insufficient funds.");

        await handler.Handle(message);

        sourceProxy.Verify(
            proxy => proxy.SendCoreAsync(
                "ReceiveStatusUpdate",
                It.Is<object?[]>(args => MatchesUpdate(args, "ACC-001", TransactionStatus.Failed, "Source account has insufficient funds.")),
                default),
            Times.Once);

        targetProxy.Verify(
            proxy => proxy.SendCoreAsync(
                "ReceiveStatusUpdate",
                It.Is<object?[]>(args => MatchesUpdate(args, "ACC-002", TransactionStatus.Failed, "Source account has insufficient funds.")),
                default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Send_Only_One_Update_When_Source_And_Target_Are_The_Same_Group()
    {
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group("ACC-001")).Returns(proxy.Object);

        var hubContext = new Mock<IHubContext<TradeHub>>();
        hubContext.SetupGet(context => context.Clients).Returns(clients.Object);

        var handler = new NotificationHandler(hubContext.Object, Mock.Of<ILogger<NotificationHandler>>());

        await handler.Handle(new TransactionStatusChangedEvent(
            Guid.NewGuid(),
            "ACC-001",
            "ACC-001",
            10m,
            "USD",
            TransactionStatus.Validated,
            TransactionStatus.Processed,
            DateTime.UtcNow));

        proxy.Verify(
            client => client.SendCoreAsync("ReceiveStatusUpdate", It.IsAny<object?[]>(), default),
            Times.Once);
    }

    private static bool MatchesUpdate(object?[] args, string accountId, TransactionStatus status, string? failureReason)
    {
        if (args.Length != 1 || args[0] is not TransactionUpdateDto dto)
        {
            return false;
        }

        return dto.AccountId == accountId
            && dto.Status == status
            && dto.FailureReason == failureReason;
    }
}
