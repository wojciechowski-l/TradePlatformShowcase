using TradePlatform.Api.Components.Pages;

namespace TradePlatform.Tests.Api;

public class RealtimeEventDeduplicatorTests
{
    [Fact]
    public void TryAccept_Should_Reject_Duplicates()
    {
        var deduplicator = new RealtimeEventDeduplicator();

        Assert.True(deduplicator.TryAccept("evt-1"));
        Assert.False(deduplicator.TryAccept("evt-1"));
    }

    [Fact]
    public void TryAccept_Should_Evict_Oldest_Event_When_Capacity_Is_Reached()
    {
        var deduplicator = new RealtimeEventDeduplicator(2);

        Assert.True(deduplicator.TryAccept("evt-1"));
        Assert.True(deduplicator.TryAccept("evt-2"));
        Assert.True(deduplicator.TryAccept("evt-3"));
        Assert.True(deduplicator.TryAccept("evt-1"));
    }
}
