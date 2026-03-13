# Failure Modes

This document catalogues the failure scenarios of the Trade Platform, the system state
each scenario produces, and how the architecture responds. All entries are derived from
the actual code paths in the codebase.

**Messaging guarantee:** At-least-once delivery. All message handlers must be idempotent.
Scenario 9 documents how the system satisfies this requirement.

The analysis is organised by failure site, moving through the request lifecycle in order.

---

## 1. API crashes before `CommitAsync()`

**Site:** `RebusSqlTransactionScopeManager.ExecuteInTransactionAsync`
**Code path:** Process dies after `action()` but before `transaction.CommitAsync()`

**State produced:**
- SQL transaction rolls back atomically.
- `TransactionRecord` is never written.
- Outbox entry is never written.
- No message reaches RabbitMQ.

**System state:** Clean. No partial state exists anywhere in the system.

**Recovery:** The client receives a failed HTTP response (connection error or 5xx).
Client-side retry creates a new transaction.

---

## 2. API crashes after `CommitAsync()`, before HTTP response returns

**Site:** `TransactionsController` → `TransactionService`
**Code path:** Commit succeeds; process dies before `202 Accepted` is sent.

**State produced:**
- `TransactionRecord` exists with `Status = Pending`.
- Outbox entry exists in SQL Server.
- Rebus background forwarder will eventually forward the message to RabbitMQ and the
  Worker will process it.

**System state:** Transaction will be processed correctly on recovery. The client does
not know this.

**Recovery:** The Rebus Outbox forwarder on the next API instance will drain the entry
and deliver `TransactionCreatedEvent` to the Worker. The client, having received no
response, may retry — the duplicate submission will create a second `TransactionRecord`
with a different ID. There is no idempotency key deduplication at the HTTP layer.

---

## 3. Rebus Outbox forwarder cannot reach RabbitMQ

**Site:** Background Rebus forwarder process within the API
**Code path:** `RebusOutbox` table has forwarded entries; RabbitMQ is unavailable.

**State produced:**
- `TransactionRecord` exists with `Status = Pending`.
- Outbox entries accumulate in the `RebusOutbox` SQL table.
- No messages are delivered to the Worker.
- The HTTP response has already returned `202 Accepted` to the client.

**System state:** Durable. No data is lost. Outbox entries survive API restarts.

**Recovery:** When RabbitMQ becomes reachable, the forwarder automatically drains the
accumulated outbox entries. All pending transactions will be processed in order. The
client will receive SignalR status updates once the backlog clears. No operator
intervention is required.

---

## 4. RabbitMQ broker restart

**Site:** RabbitMQ service
**Code path:** Broker goes offline and restarts.

**State produced:**
- In-flight messages not yet acknowledged are re-queued by RabbitMQ on restart (queues
  are durable — `durable: true` in queue declarations).
- The API's Rebus outbox forwarder will re-attempt delivery once the broker is reachable.
- The Worker's Rebus consumer will reconnect and resume processing.

**System state:** No messages are lost. Durable queues survive broker restarts.

**Recovery:** Automatic. Rebus handles broker reconnection. Outbox forwarder resumes.

---

## 5. Worker crashes during `TransactionCreatedHandler.Handle()`

**Sub-case A: Crash before `SaveChangesAsync()` and `bus.Publish()` inside the scope**

**State produced:**
- `ExecuteInTransactionAsync` transaction is rolled back.
- `TransactionRecord.Status` remains `Pending`.
- No `TransactionProcessedEvent` is published.
- RabbitMQ has not yet acknowledged the message (Rebus uses client acknowledgement).

**Recovery:** Rebus re-delivers `TransactionCreatedEvent` (up to 3 attempts via
`SimpleRetryStrategy`). The handler's idempotency guard
(`if (transaction.Status == Processed) return`) prevents double-processing if a prior
partial attempt had already committed.

---

**Sub-case B: Crash after `transaction.CommitAsync()` but before RabbitMQ acknowledgement**

**State produced:**
- `TransactionRecord.Status = Processed` is committed.
- `TransactionProcessedEvent` outbox entry is committed.
- Rebus has not yet acknowledged the original `TransactionCreatedEvent` to RabbitMQ.

**Recovery:** RabbitMQ re-delivers `TransactionCreatedEvent`. The handler's idempotency
guard (`Status == Processed`) short-circuits the handler body, preventing a second status
update. The previously committed `TransactionProcessedEvent` outbox entry will be
forwarded on the next Worker startup and the client will receive its SignalR update.

---

## 6. Worker exhausts all retry attempts (message dead-lettered)

**Site:** `SimpleRetryStrategy(maxDeliveryAttempts: 3)` in `TradePlatform.Worker/Program.cs`
**Code path:** `TransactionCreatedHandler.Handle()` throws on all 3 delivery attempts.

**State produced:**
- `TransactionCreatedEvent` is moved to the Rebus dead-letter queue.
- `TransactionRecord.Status` remains `Pending` indefinitely.
- No `TransactionProcessedEvent` is published.
- No SignalR update is sent to the client.

**System state:** The transaction is orphaned in `Pending`. The client has no way to
know the transaction will not complete.

**Known gap:** `TransactionStatus.Failed` exists in the domain enum but is not wired
to dead-letter handling. No dead-letter consumer exists. See `Decisions.md` ADR-010.

**Recovery:** Manual inspection of the dead-letter queue is required. An operator must
determine the root cause and either re-queue the message or manually update the record.

---

## 7. Redis backplane unavailable

**Site:** SignalR Redis backplane in `TradePlatform.Api/Program.cs`
**Code path:** `NotificationHandler` calls `hubContext.Clients.Group(...).SendAsync(...)`
while Redis is unavailable.

**State produced:**
- The SignalR push fails or reaches only clients connected to the current replica.
- `TransactionRecord.Status` has already been committed as `Processed` in SQL Server.
- The transaction was processed correctly. Only the real-time notification is lost.

**System state:** Durable state is correct. Only the push notification is affected.

**Recovery:** The client will not receive a real-time update. Since `TransactionRecord`
in the database reflects the correct final state, a page refresh or explicit status poll
would surface the correct status. There is no client-side polling fallback in the current
implementation.

---

## 8. SQL Server unavailable at startup

**Site:** `migrator` service and `api`/`worker` service startup

**State produced:**
- The `migrator` service fails, which causes `api` and `worker` to not start
  (`condition: service_completed_successfully` dependency in `docker-compose.yml`).
- The entire stack remains down.

**Recovery:** Restart the SQL Server container. Re-run `docker compose up`. The
`docker-compose.yml` health check (`sqlcmd SELECT 1`) gates dependent services.

---

## 9. Duplicate `TransactionCreatedEvent` delivery (at-least-once messaging)

**Site:** Worker consumer — Rebus at-least-once delivery guarantee
**Code path:** Rebus re-delivers a message the Worker already processed (e.g., after
crash sub-case B above, or a network partition causing broker re-delivery).

**State produced without guard:** Double-processing — status updated twice, two
`TransactionProcessedEvent` published, two SignalR pushes sent.

**State produced with guard:**

```csharp
if (transaction.Status == TransactionStatus.Processed)
{
    LogAlreadyProcessed(logger, evt.TransactionId);
    return;
}
```

The handler returns early. The duplicate message is acknowledged and discarded.
No second write, no second publish.

**System state:** Idempotent. The guard in `TransactionCreatedHandler` makes duplicate
delivery safe for the happy path. The guard relies on the `TransactionRecord` already
existing — if it was never written (API crash before commit), there is no record to
check, but that scenario is handled by case 1 above (clean rollback, no delivery).

---

## 10. Ownership cache stale after account transfer

**Site:** `DbAccountOwnershipService` — `IMemoryCache` with 30-second TTL
**Code path:** Account ownership is reassigned; a cached entry for the previous owner
exists within the TTL window.

**State produced:**
- The previous owner can still pass the ownership check for up to 30 seconds after the
  change, because positive results are cached.
- The new owner with no cache entry will fall back to the DB query and get the correct
  result immediately.

**System state:** A bounded (≤ 30 second) window of stale positive authorization. Negative
results are never cached, so a revoked owner will see the correct denial once the cache
entry expires or the JWT is refreshed.

**Note:** Account ownership transfer is not implemented in the current codebase. This
failure mode is recorded for completeness if that feature is added.
