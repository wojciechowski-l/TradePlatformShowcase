namespace TradePlatform.Core.Entities
{
    /// <summary>
    /// Sentinel values for OutboxMessage.ProcessedAtUtc.
    ///
    /// The column carries three states using a single nullable DateTime:
    ///   NULL            → unprocessed, available for pickup
    ///   InFlight        → claimed by a worker via UPDLOCK, not yet confirmed
    ///   real timestamp  → successfully published (or dead-lettered, see LastError)
    ///
    /// The sentinel must be a value no real publish would produce so that
    /// stuck in-flight rows (e.g. after a crash) remain queryable and recoverable.
    /// </summary>
    public static class OutboxStatus
    {
        public static readonly DateTime InFlight = DateTime.MinValue;
    }
}
