# Architecture

## Purpose

This document describes the runtime architecture of the Trade Platform Showcase as derivable
from the source code, Docker Compose files, and configuration. It is written to be accurate
to the implementation, not aspirational.

---

## Solution Structure

```
TradePlatform.Core           — Domain entities, interfaces, DTOs, value objects, constants.
                               No infrastructure dependencies.

TradePlatform.Infrastructure — EF Core context, Rebus scope manager, ownership service,
                               migrations. Depends on Core only.

TradePlatform.Api            — ASP.NET Core 10 host: REST controllers, SignalR hub,
                               Rebus event handlers. Depends on Core + Infrastructure.

TradePlatform.Worker         — .NET Worker host: Rebus consumer, transaction processor.
                               Depends on Core + Infrastructure.

TradePlatform.Tests          — xUnit unit + integration tests. Testcontainers-backed.

Client                       — React 19 / TypeScript / Vite / Material UI SPA.
```

Dependency direction:

```
Api  ──► Infrastructure ──► Core ◄── Worker ──► Infrastructure
                                ▲
                             (shared)
```

`Core` has zero outbound project dependencies.

---

## Runtime Topology

```
Browser
  │  HTTP (REST)           Bearer JWT
  ├────────────────────► TradePlatform.Api (:8080)
  │  WebSocket (SignalR)   Bearer JWT
  └────────────────────► TradePlatform.Api (:8080)
                                  │
                     ┌────────────┴──────────────┐
                     │                           │
               SQL Server                    RabbitMQ
               (EF Core 10)          ┌────────────────────┐
               RebusOutbox table     │  trade-orders queue │
                                     └──────────┬─────────┘
                                                │ bus.Send() via Outbox
                                                ▼
                                     TradePlatform.Worker
                                                │
                                                │ bus.Publish() via Outbox
                                                ▼
                                     RabbitMQ (topic exchange)
                                                │ bus.Subscribe<TransactionProcessedEvent>()
                                                ▼
                                     TradePlatform.Api (NotificationHandler)
                                                │
                                          Redis backplane
                                                │
                                          SignalR group push
                                                │
                                            Browser
```

### Infrastructure Services

| Service    | Image                            | Port(s)              | Purpose                               |
|------------|----------------------------------|----------------------|---------------------------------------|
| sql-server | mssql/server:2022-latest         | 1433                 | Primary data store + Outbox table     |
| rabbitmq   | rabbitmq:4-management            | 5672, 15672, 15692   | Message broker                        |
| redis      | redis:7-alpine                   | 6379                 | SignalR Redis backplane               |
| prometheus | prom/prometheus                  | 9090                 | Metrics scraping                      |
| grafana    | grafana/grafana                  | 3100                 | Dashboard visualisation               |
| seq        | datalust/seq                     | 5341                 | Structured log aggregation            |
| migrator   | Api image (`--migrate-only`)     | —                    | One-shot EF Core migration runner     |
| client     | Nginx-served Vite build          | 3000                 | React SPA                             |

---

## Request Lifecycle — Trade Submission

**1. Authentication**
Client calls `POST /api/auth/login` (ASP.NET Identity endpoint). The JWT issued contains
the custom claim `urn:tradeplatform:accountid`, injected at token generation by
`TradeUserClaimsPrincipalFactory`.

**2. HTTP write — `POST /api/transactions`**
Bearer token attached. `[Authorize]` gate validates the JWT. `TransactionsController`
calls `IAccountOwnershipService.IsOwnerAsync` against `SourceAccountId`. Returns `403`
if the caller does not own the account. If an `Idempotency-Key` header is present, the
key is passed through to `TransactionService`; deduplication occurs inside the
transaction scope (step 3). A concurrent duplicate that loses the `UNIQUE` constraint
race returns `409 Conflict`.

**3. Atomic write — `TransactionService` inside `RebusSqlTransactionScopeManager`**

```csharp
using var transaction = await dbContext.Database.BeginTransactionAsync();
using var rebusScope  = new RebusTransactionScope();

rebusScope.UseOutbox((SqlConnection)dbConnection, (SqlTransaction)dbTransaction);

// inserts TransactionRecord (Status = Pending) + stages TransactionCreatedEvent
await action();

await rebusScope.CompleteAsync();   // marks outbox entry for forwarding
await transaction.CommitAsync();    // single SQL commit: record + outbox entry
```

The `TransactionRecord` (Status = `Pending`), the `IdempotencyKey` row (when a key is
present), and the outbox entry are committed atomically. The `UNIQUE` index on
`(Key, UserId)` in `IdempotencyKeys` enforces exactly-once semantics at the database
level for keyed requests. If the key already exists (a retry of a previously committed
request), the handler returns the stored `TransactionId` immediately without a new insert.

**4. Outbox forwarder (background, Rebus)**
After the commit, the Rebus background forwarder reads from `RebusOutbox` and publishes
`TransactionCreatedEvent` to the `trade-orders` queue on RabbitMQ. This only executes
post-commit — a rolled-back transaction leaves no outbox entry.

**5. Worker — `TransactionCreatedHandler`**
Receives `TransactionCreatedEvent` from `trade-orders`. Inside its own
`RebusSqlTransactionScopeManager` scope:
- Idempotency guard: skips if `TransactionRecord.Status == Processed` already.
- Updates `TransactionRecord.Status = Processed`.
- Calls `bus.Publish(new TransactionProcessedEvent(...))` — staged in the same outbox
  transaction, forwarded to the RabbitMQ topic exchange post-commit.

**6. API — `NotificationHandler`**
At startup, the API calls `bus.Subscribe<TransactionProcessedEvent>()`, binding its
`notifications` queue to the RabbitMQ topic exchange for this event type.
`NotificationHandler` receives the event and calls:

```csharp
await hubContext.Clients.Group(message.AccountId)
    .SendAsync("ReceiveStatusUpdate", dto);
```

The Redis backplane (channel prefix `TradePlatform`) ensures the group message reaches
the correct API replica regardless of which replica holds the client's WebSocket.

**7. Client**
Receives `ReceiveStatusUpdate` on the SignalR connection and updates the UI.

---

## Identity and Ownership Model

**Identity origin:** ASP.NET Core Identity. JWT issued at login by
`TradeUserClaimsPrincipalFactory`, which embeds the user's first account ID as the custom
claim `urn:tradeplatform:accountid`.

**Ownership resolution — `DbAccountOwnershipService`:**

```
Step 1: Compare urn:tradeplatform:accountid claim value to the requested accountId.
        → Match: return true immediately (zero DB cost).

Step 2: Query: SELECT 1 FROM Accounts WHERE Id = @accountId AND OwnerId = @userId
        → Positive result cached in IMemoryCache for 30 seconds.
        → Negative results are NOT cached.
```

**Enforcement points (independent):**

| Boundary           | File                         | Mechanism                          | Failure response        |
|--------------------|------------------------------|------------------------------------|-------------------------|
| HTTP write path    | `TransactionsController.cs`  | `IsOwnerAsync` before service call | HTTP 403 Forbidden      |
| SignalR group join | `TradeHub.cs`                | `IsOwnerAsync` before AddToGroup   | HubException (rejected) |

The two checks are independently enforced. A bypass of one does not bypass the other.

---

## Messaging Topology

| Producer              | Mechanism    | Message type               | Routing               | Consumer              |
|-----------------------|--------------|----------------------------|-----------------------|-----------------------|
| TradePlatform.Api     | `bus.Send()` | `TransactionCreatedEvent`  | TypeBased → `trade-orders` | TradePlatform.Worker |
| TradePlatform.Worker  | `bus.Publish()` | `TransactionProcessedEvent` | Topic exchange (pub/sub) | TradePlatform.Api  |

- `TransactionCreatedEvent` uses point-to-point Send with explicit TypeBased routing.
- `TransactionProcessedEvent` uses pub/sub Publish. The API subscribes at startup via
  `bus.Subscribe<TransactionProcessedEvent>()`.
- Both queues are durable. `SimpleRetryStrategy(maxDeliveryAttempts: 3)` is configured
  on both services. After 3 failed deliveries, messages are dead-lettered.

**Delivery guarantee:** At-least-once. All message handlers must be idempotent.
`TransactionCreatedHandler` satisfies this via a status guard before any write.

**Consistency model:** Transactions are eventually consistent. The client observes status
transitions asynchronously via SignalR after the Worker commits.

```
Pending  →  Processed   (happy path — SignalR ReceiveStatusUpdate)
Pending  →  Pending     (dead-lettered — no terminal state; see ADR-010)
```

**Known gap:** `TransactionStatus.Failed` is defined in the domain enum but is not wired
to any state transition. A dead-lettered message leaves `TransactionRecord` in `Pending`
indefinitely. See `Decisions.md` ADR-010.

---

## Observability

| Pillar  | Technology                                   | Scope                                                                    |
|---------|----------------------------------------------|--------------------------------------------------------------------------|
| Tracing | OpenTelemetry + `Rebus.OpenTelemetry`        | TraceId propagated through RabbitMQ message headers; full cross-service span in Seq |
| Metrics | Prometheus + Grafana                         | ASP.NET runtime, .NET GC heap, `trades_created_total` counter, `trade_amount` histogram, RabbitMQ queue depth |
| Logging | Serilog → Seq                                | Structured logs enriched with TraceId; correlated across API and Worker  |

Prometheus scrape targets:
- API at `:8080` via ASP.NET Core `MapPrometheusScrapingEndpoint()`
- Worker at `:9091` via `AddPrometheusHttpListener`

Grafana dashboards are provisioned-as-code from `observability/grafana/provisioning/`
and load automatically on container startup.

---

## Test Architecture

**Layer 1 — Unit tests** (`TradePlatform.Tests`): `TransactionService`,
`TransactionCreatedHandler`, and `TransactionDtoValidator` tested in isolation. Mocks
for `IBus`, `ITransactionScopeManager`, `ITradeContext`.

**Layer 2 — Integration tests** (`TradePlatform.Tests/Integration`): Full ASP.NET Core
pipeline via `WebApplicationFactory<Program>`. Real SQL Server and RabbitMQ instances via
Testcontainers (`MsSqlContainer`, `RabbitMqContainer`). Authentication replaced by
`TestAuthHandler`, which injects real-shaped claims (`ClaimTypes.NameIdentifier`,
`urn:tradeplatform:accountid`) via HTTP headers. Ownership checks, FluentValidation
filter, and full Rebus pipeline run unmodified.

**Layer 3 — E2E tests** (`Client/e2e`): Playwright against the full
`docker-compose.test.yml` stack. Covers registration, login, trade submission, and
SignalR status update receipt.

---

## Scaling Considerations

**API** is horizontally scalable. The Redis backplane ensures SignalR group messages
reach the correct replica. All authoritative state lives in SQL Server or RabbitMQ.

**Worker** is horizontally scalable. `SetNumberOfWorkers(5)` controls in-process
concurrency. Multiple Worker containers compete on the `trade-orders` queue via standard
RabbitMQ consumer competition — no explicit coordination is required. The idempotency
guard in `TransactionCreatedHandler` (status check before update) prevents double-processing
under competing consumers.

**Ownership cache** is process-local (`IMemoryCache`). Under horizontal API scale, each
replica independently warms its cache on first ownership check. The 30-second TTL bounds
the staleness window.
