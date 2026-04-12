using System.Diagnostics.Metrics;
using TradePlatform.Core.Constants;

namespace TradePlatform.Infrastructure.Services;

public static class MessagingMetrics
{
    public static readonly string MeterName = "TradePlatform.Messaging";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static long _inboxRows;
    private static long _idempotencyRows;
    private static long _outboxRows;
    private static long _ordersDeadLetterRows;
    private static long _notificationsDeadLetterRows;

    private static readonly Counter<long> InboxDuplicateHitsCounter = Meter.CreateCounter<long>(
        "messaging_inbox_duplicate_hits_total",
        description: "Number of duplicate inbox deliveries skipped.");

    private static readonly Counter<long> RequestIdempotencyHitsCounter = Meter.CreateCounter<long>(
        "messaging_request_idempotency_hits_total",
        description: "Number of API idempotency key hits served from existing state.");

    private static readonly Counter<long> RetryAttemptsCounter = Meter.CreateCounter<long>(
        "messaging_retry_attempts_total",
        description: "Number of handler executions caused by message redelivery.");

    private static readonly Counter<long> StatusUpdatesPushedCounter = Meter.CreateCounter<long>(
        "messaging_status_updates_pushed_total",
        description: "Number of realtime status updates pushed to SignalR groups.");

    private static readonly Counter<long> RetentionDeletesCounter = Meter.CreateCounter<long>(
        "messaging_retention_deleted_rows_total",
        description: "Number of rows deleted by retention sweeps.");

    static MessagingMetrics()
    {
        Meter.CreateObservableGauge<long>(
            "messaging_storage_rows",
            ObserveStorageRows,
            description: "Current number of retained rows in messaging reliability tables.");

        Meter.CreateObservableGauge<long>(
            "messaging_dead_letter_backlog",
            ObserveDeadLetterRows,
            description: "Current dead-letter queue backlog.");
    }

    public static void RecordInboxDuplicate(string consumer) =>
        InboxDuplicateHitsCounter.Add(1, new KeyValuePair<string, object?>("consumer", consumer));

    public static void RecordRequestIdempotencyHit() =>
        RequestIdempotencyHitsCounter.Add(1);

    public static void RecordRetryAttempt(string handler, string messageType) =>
        RetryAttemptsCounter.Add(1,
            new KeyValuePair<string, object?>("handler", handler),
            new KeyValuePair<string, object?>("message_type", messageType));

    public static void RecordStatusUpdatePush(string accountId, string status) =>
        StatusUpdatesPushedCounter.Add(1,
            new KeyValuePair<string, object?>("account_id", accountId),
            new KeyValuePair<string, object?>("status", status));

    public static void RecordRetentionDelete(string table, long deletedRows)
    {
        if (deletedRows <= 0)
        {
            return;
        }

        RetentionDeletesCounter.Add(deletedRows, new KeyValuePair<string, object?>("table", table));
    }

    public static void UpdateStorageRows(long inboxRows, long idempotencyRows, long outboxRows)
    {
        Interlocked.Exchange(ref _inboxRows, inboxRows);
        Interlocked.Exchange(ref _idempotencyRows, idempotencyRows);
        Interlocked.Exchange(ref _outboxRows, outboxRows);
    }

    public static void UpdateDeadLetterRows(long ordersDeadLetterRows, long notificationsDeadLetterRows)
    {
        Interlocked.Exchange(ref _ordersDeadLetterRows, ordersDeadLetterRows);
        Interlocked.Exchange(ref _notificationsDeadLetterRows, notificationsDeadLetterRows);
    }

    private static IEnumerable<Measurement<long>> ObserveStorageRows()
    {
        yield return new Measurement<long>(
            Interlocked.Read(ref _inboxRows),
            new KeyValuePair<string, object?>("table", "InboxMessages"));
        yield return new Measurement<long>(
            Interlocked.Read(ref _idempotencyRows),
            new KeyValuePair<string, object?>("table", "IdempotencyKeys"));
        yield return new Measurement<long>(
            Interlocked.Read(ref _outboxRows),
            new KeyValuePair<string, object?>("table", "RebusOutbox"));
    }

    private static IEnumerable<Measurement<long>> ObserveDeadLetterRows()
    {
        yield return new Measurement<long>(
            Interlocked.Read(ref _ordersDeadLetterRows),
            new KeyValuePair<string, object?>("queue", MessagingConstants.OrdersDeadLetterQueue));

        yield return new Measurement<long>(
            Interlocked.Read(ref _notificationsDeadLetterRows),
            new KeyValuePair<string, object?>("queue", MessagingConstants.NotificationsDeadLetterQueue));
    }
}
