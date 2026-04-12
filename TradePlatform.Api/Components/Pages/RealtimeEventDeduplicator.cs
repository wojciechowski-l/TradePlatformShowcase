namespace TradePlatform.Api.Components.Pages;

public sealed class RealtimeEventDeduplicator(int capacity = 512)
{
    private readonly int _capacity = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

    private readonly Queue<string> _orderedEventIds = [];
    private readonly HashSet<string> _seenEventIds = [];
    private readonly object _lock = new();

    public bool TryAccept(string? eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        lock (_lock)
        {
            if (!_seenEventIds.Add(eventId))
            {
                return false;
            }

            _orderedEventIds.Enqueue(eventId);

            while (_orderedEventIds.Count > _capacity)
            {
                _seenEventIds.Remove(_orderedEventIds.Dequeue());
            }

            return true;
        }
    }
}
