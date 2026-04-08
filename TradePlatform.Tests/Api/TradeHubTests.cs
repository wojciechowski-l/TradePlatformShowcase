using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using System.Security.Claims;
using TradePlatform.Api.Hubs;
using TradePlatform.Core.Interfaces;

namespace TradePlatform.Tests.Api;

public class TradeHubTests
{
    [Fact]
    public async Task JoinAccountGroup_Should_Add_User_To_Group_When_Ownership_Check_Passes()
    {
        var ownershipService = new Mock<IAccountOwnershipService>();
        ownershipService
            .Setup(service => service.IsOwnerAsync(It.IsAny<ClaimsPrincipal>(), "ACC-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var groups = new RecordingGroupManager();
        var hub = CreateHub(ownershipService.Object, groups, CreatePrincipal("user-1"));

        await hub.JoinAccountGroup("ACC-001");

        Assert.Contains(("conn-1", "ACC-001"), groups.Added);
    }

    [Fact]
    public async Task JoinAccountGroup_Should_Throw_When_User_Does_Not_Own_Account()
    {
        var ownershipService = new Mock<IAccountOwnershipService>();
        ownershipService
            .Setup(service => service.IsOwnerAsync(It.IsAny<ClaimsPrincipal>(), "ACC-999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var groups = new RecordingGroupManager();
        var hub = CreateHub(ownershipService.Object, groups, CreatePrincipal("user-9"));

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.JoinAccountGroup("ACC-999"));

        Assert.Contains("user-9", exception.Message);
        Assert.Empty(groups.Added);
    }

    private static TradeHub CreateHub(
        IAccountOwnershipService ownershipService,
        RecordingGroupManager groups,
        ClaimsPrincipal user)
    {
        return new TradeHub(ownershipService)
        {
            Context = new TestHubCallerContext("conn-1", user),
            Groups = groups
        };
    }

    private static ClaimsPrincipal CreatePrincipal(string userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "Test"));
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];
        public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class TestHubCallerContext(string connectionId, ClaimsPrincipal user) : HubCallerContext
    {
        private readonly Dictionary<object, object?> _items = [];

        public override string ConnectionId => connectionId;
        public override string? UserIdentifier => user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        public override ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features => new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
