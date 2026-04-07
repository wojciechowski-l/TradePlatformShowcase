# Architecture Decision Records

Each record documents a concrete decision present in the codebase, the alternatives
considered, and the reasoning. All entries are derived from the implemented code.

---

## ADR-001 — Rebus over direct RabbitMQ.Client for application messaging

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs`, `TradePlatform.Worker/Program.cs`

**Decision:** Use Rebus as the messaging abstraction layer over RabbitMQ rather than
driving `RabbitMQ.Client` directly for application-level message dispatch.

**Reasoning:** Rebus provides typed message dispatch, TypeBased routing, built-in
`SimpleRetryStrategy` with dead-lettering, and first-class Outbox support via
`Rebus.SqlServer`. Implementing equivalent guarantees over raw `RabbitMQ.Client` would
require significant bespoke infrastructure.

`RabbitMQ.Client` is still used directly in `ApiIntegrationTests.cs` to declare queues
before tests run. This is correct — queue topology declaration is infrastructure setup,
not application messaging, and Testcontainers does not pre-declare queues.

**Trade-off:** Adds a framework dependency and abstraction layer. Accepted because the
surface area is small and explicitly configured; no opaque conventions are relied upon.

---

## ADR-002 — Transactional Outbox over direct bus.Send()

**Status:** Implemented
**Files:** `TradePlatform.Infrastructure/Services/RebusSqlTransactionScopeManager.cs`

**Decision:** All message publications are performed inside a `RebusTransactionScope`
bound to the same ADO.NET transaction as the EF Core write. Messages are written to the
`RebusOutbox` SQL table and forwarded to RabbitMQ only after the database commit succeeds.

**Reasoning:** Calling `bus.Send()` outside a transaction creates a dual-write window:
the database commit can succeed while the message publish fails, leaving a
`TransactionRecord` with no corresponding processing event. The Outbox pattern eliminates
this window. If `CommitAsync()` throws, neither the domain record nor the outbox entry
exists. This delivers at-least-once semantics without a distributed transaction coordinator.

**Trade-off:** Requires the `RebusOutbox` SQL table and a background forwarding process.
Both are provided by `Rebus.SqlServer` with no custom maintenance cost.

---

## ADR-003 — Explicit TypeBased routing; pub/sub Publish for worker lifecycle events

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs`, `TradePlatform.Worker/Program.cs`,
`TradePlatform.Core/Constants/MessagingConstants.cs`

**Decision:** Point-to-point commands (`TransactionCreatedEvent`) use TypeBased routing
with explicit queue mapping declared in `Program.cs`. Worker-originated events
(`TransactionStatusChangedEvent`) use `bus.Publish()` with the API subscribing via
`bus.Subscribe<TransactionStatusChangedEvent>()` at startup. The Worker's routing table
contains no entry for `TransactionStatusChangedEvent` because `Publish` uses the RabbitMQ
topic exchange, not the TypeBased send table.

This separation means:
- API → Worker: point-to-point Send, TypeBased, explicit queue target.
- Worker → API: pub/sub Publish, exchange-based, subscriber-driven.

**Reasoning:** `TransactionCreatedEvent` is a command with a single known consumer —
point-to-point Send is the correct Rebus primitive. `TransactionStatusChangedEvent` is a
domain event that any number of consumers could subscribe to — pub/sub Publish is the
correct primitive and allows future consumers to be added without modifying the Worker.

Explicit TypeBased mapping for Send ensures every command's destination is traceable from
a single declaration site, preventing accidental queue mis-targeting.

**Trade-off:** Developers must understand which Rebus primitive (Send vs Publish) applies
to which message type. Mixing them silently fails at runtime. This is mitigated by the
clear separation in `MessagingConstants` and the explicit `bus.Subscribe<>()` call at
API startup.

---

## ADR-004 — Redis backplane for SignalR

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs` (`AddStackExchangeRedis`)

**Decision:** SignalR is configured with a Redis backplane using channel prefix
`TradePlatform` (via `RedisChannel.Literal`).

**Reasoning:** Without a backplane, `IHubContext.Clients.Group(accountId).SendAsync(...)`
only reaches clients connected to the same API process. `TransactionStatusChangedEvent`
arrives at an arbitrary API replica via RabbitMQ. That replica must be able to forward the
SignalR push to whichever replica holds the WebSocket. Redis pub/sub provides this
cross-replica fan-out.

**Trade-off:** Adds a Redis dependency to the API. Redis is already present in the stack
for this purpose. The channel prefix isolates this application's backplane traffic if
Redis is shared.

---

## ADR-005 — Two-step ownership check (principal claim → DB with cache)

**Status:** Implemented
**Files:** `TradePlatform.Infrastructure/Services/DbAccountOwnershipService.cs`,
`TradePlatform.Api/Infrastructure/TradeUserClaimsPrincipalFactory.cs`

**Decision:** `DbAccountOwnershipService.IsOwnerAsync` first compares the
`urn:tradeplatform:accountid` principal claim to the requested account ID. On a miss, it falls
back to a database query. Positive DB results are cached in `IMemoryCache` for 30 seconds.
Negative results are never cached.

**Reasoning:** The ownership check is on the hot path for every trade submission and every
SignalR group join. A pure DB query per request does not scale. The principal claim handles the
common case at zero cost. The 30-second cache absorbs reconnect storms after a service
restart without requiring distributed cache coordination. Negative results are not cached
to prevent a transient denial from persisting across a subsequent legitimate ownership
grant.

**Trade-off:** The claim is embedded at sign-in time. If account ownership changes between
session issuance and expiry, the claim will be stale until the next login. The cache is
process-local; under horizontal API scale, each replica caches independently, creating
bounded per-replica inconsistency windows within the TTL.

---

## ADR-006 — ASP.NET Core Identity cookies with custom form endpoints

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs`,
`TradePlatform.Api/Endpoints/AuthEndpoints.cs`,
`TradePlatform.Api/Infrastructure/TradeUserClaimsPrincipalFactory.cs`

**Decision:** Use ASP.NET Core Identity with the application cookie scheme and small
custom form-post endpoints for login, registration, and logout, rather than exposing a
browser-facing token API.

**Reasoning:** The browser now runs against a single-origin Blazor Web App hosted by the
API. Cookie auth fits that topology better than browser-managed JWT plumbing: the forms
post directly to same-origin endpoints, the browser automatically carries the session for
HTML, REST, and SignalR requests, and the UI no longer needs client token storage or
refresh logic.

**Trade-off:** The auth flow is browser-session oriented rather than API-token oriented,
which is ideal for this showcase UI but less reusable for third-party API consumers.
Accepted because the repo's primary interactive experience is now the hosted Blazor app.

---

## ADR-007 — Testcontainers for integration tests

**Status:** Implemented
**Files:** `TradePlatform.Tests/Integration/ApiIntegrationTests.cs`,
`TradePlatform.Tests/Worker/TransactionCreatedHandlerTests.cs`

**Decision:** Integration tests spin up real SQL Server and RabbitMQ containers via
Testcontainers (`MsSqlContainer`, `RabbitMqContainer`) rather than using in-memory
database providers or broker mocks.

**Reasoning:** In-memory database providers do not enforce SQL constraints, foreign key
relationships, or transactional semantics. The Outbox pattern's correctness depends on
real SQL transaction behaviour. Rebus routing correctness depends on a real broker.
Testcontainers provides ephemeral, isolated, real infrastructure per test run with no
shared state between runs.

**Trade-off:** Tests take longer to start due to container pull and initialisation. This
is mitigated by using `IAsyncLifetime` fixtures to share container instances across all
tests in a class, and by pre-declaring queues via `RabbitMQ.Client` before the test host
starts (the host's Rebus configuration does not declare consumer queues).

---

## ADR-008 — Dedicated migrator service for EF Core migrations

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs` (`--migrate-only` branch), `docker-compose.yml`

**Decision:** Database migrations are applied by a dedicated ephemeral Docker service —
the Api image invoked with `--migrate-only` — that exits after `MigrateAsync()` completes.
The Api and Worker services use `condition: service_completed_successfully` as a dependency
on the migrator.

**Reasoning:** Applying migrations inside `Program.cs` on every startup creates a race
condition under horizontal deployment — multiple replicas attempt concurrent migrations.
It also means a migration failure terminates a live service instance. Separating migration
into a one-shot service makes the deployment sequence explicit: the schema is guaranteed
correct before any replica starts accepting traffic.

**Trade-off:** Adds a container to the Compose topology. The migrator shares the Api
Dockerfile with no additional image size cost.

---

## ADR-009 — OpenTelemetry with Rebus instrumentation for cross-service tracing

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs`, `TradePlatform.Worker/Program.cs`
(`AddRebusInstrumentation()`, `AddSource("Rebus")`)

**Decision:** OpenTelemetry is configured on both services with `AddRebusInstrumentation()`.
This propagates trace context through RabbitMQ message headers, producing a single trace
spanning the API entry point, the Outbox publish, the Worker message consumption, the
domain write, and the notification push back through the API.

**Reasoning:** Without cross-service propagation, diagnosing latency or failures in the
async processing chain requires manually correlating logs across services by transaction
ID. With propagation, the full execution path is visible as a single trace in Seq.

**Trade-off:** Both services must participate. The Worker has no ASP.NET Core HTTP
instrumentation (it exposes no HTTP endpoints), so only Rebus and .NET runtime metrics
are registered there.

---

## ADR-010 — Explicit transaction lifecycle with business-level failure states

**Status:** Implemented, with an infrastructure gap remaining
**Files:** `TradePlatform.Core/Entities/TransactionRecord.cs` (enum definition),
`TradePlatform.Worker/Handlers/TransactionCreatedHandler.cs`

**Decision:** Transactions use an explicit lifecycle:

`Pending -> Validated -> Processing -> Processed/Failed`

`Failed` is now a real business outcome for deterministic application-level rejections.
Dead-lettered infrastructure failures remain separate and can still leave a transaction
pre-terminal.

**Reasoning:** Two distinct failure classes are treated differently:

1. **Infrastructure failure** — Rebus exhausts 3 delivery attempts and dead-letters the
   message. The transaction record does not receive a business terminal state.
2. **Application failure** — The transaction is structurally valid but cannot be processed
   (e.g., insufficient funds). This now transitions the record to `Failed` and emits a
   lifecycle event with a reason.

This makes business rejections visible to users without conflating them with transport
faults.

**Remaining gap:** dead-lettered infrastructure failures still have no dedicated terminal
mapping or remediation consumer.

---

## ADR-011 — FluentValidation as MVC auto-validation filter

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs` (`AddFluentValidationAutoValidation`),
`TradePlatform.Core/DTOs/TransactionDtoValidator.cs`

**Decision:** FluentValidation validators are applied via
`SharpGrip.FluentValidation.AutoValidation.Mvc`, executing validation as an action filter
before the controller body runs.

**Reasoning:** Structural validation (source ≠ target account, positive amount, valid
currency code) does not require application services. Executing it as an MVC filter
rejects invalid requests with a structured `400 Bad Request` before any infrastructure
is touched, without requiring manual `ModelState` checks in every controller action.

**Trade-off:** Validation failure responses are formatted by the `SharpGrip` filter, not
a custom response shape. Accepted for this project's scope.

---

## ADR-012 — Currency as a value object

**Status:** Implemented
**Files:** `TradePlatform.Core/ValueObjects/Currency.cs`

**Decision:** Currency is modelled as a value object with a private constructor and a
`FromCode(string)` factory method, rather than as a raw `string` field.

**Reasoning:** A raw string field accepts any value, deferring validation to runtime or
the database. The value object enforces a valid ISO currency code format at the domain
boundary — invalid codes cannot be constructed. This eliminates an entire class of invalid
state from the model and makes the constraint visible in the type system rather than in a
validator or a check constraint in the schema.

**Trade-off:** Requires an EF Core value conversion to persist to the database. This is
standard EF Core configuration and adds no meaningful complexity.

---

## ADR-013 — HTTP idempotency keys with database-enforced uniqueness

**Status:** Implemented
**Files:** `TradePlatform.Api/Controllers/TransactionsController.cs`,
`TradePlatform.Infrastructure/Services/TransactionService.cs`,
`TradePlatform.Core/Entities/IdempotencyKey.cs`,
`TradePlatform.Infrastructure/Data/TradeContext.cs`,
`TradePlatform.Api/Components/Pages/Home.razor`

**Decision:** `POST /api/transactions` accepts an optional `Idempotency-Key` header
(UUID). The key is stored in the `IdempotencyKeys` table, scoped per user, with a 24-hour
TTL. The check, the `IdempotencyKey` row insert, and the `TransactionRecord` insert all
occur inside the same `RebusSqlTransactionScopeManager` transaction. A `UNIQUE` index on
`(Key, UserId)` is the enforcement point. The server honours reused keys when the client
resubmits the same logical operation.

**Reasoning:** Without this, a manual resubmit after a lost response (Failure Mode 2)
creates a duplicate `TransactionRecord`. Application-level read-check-then-insert without
a unique constraint is a TOCTOU race: two concurrent requests can both pass the read check
and both commit. The UNIQUE index makes the database the race arbiter — the second commit
throws `DbUpdateException` with SQL error 2601/2627, which the controller maps to
`409 Conflict`. The feature is opt-in: requests without the header bypass the check and
behave as before.

**Alternatives considered:**
- *Server-generated key returned on first response, supplied on retry:* Requires a
  two-phase protocol and a client that knows it is retrying. Not compatible with the
  manual-resubmit scenario where the client has no prior response to extract a key from.
- *Application-layer check only (no unique constraint):* Subject to the TOCTOU race
  under concurrent requests with the same key. Rejected.
- *Redis-based key cache:* Adds a dependency for a single check already backed by SQL
  Server. The `IdempotencyKeys` table participates in the same transaction as the domain
  write, which a Redis check cannot. Rejected.

**Current client behaviour:**
- Generated: `Guid.NewGuid().ToString()` at submit time in the Blazor dashboard.
- Transmitted: `Idempotency-Key` request header on each submit click.
- Rotated: automatically on the next submit because a new key is generated per click.
- Preserved: not currently preserved across failed retries in the Blazor client.

**Server-side capability:**
- The API still supports true idempotent retry semantics when the same key is reused.
- Expired: server-side, entries older than 24 hours are excluded from lookup. Cleanup
  of expired rows is not yet automated; the `IX_IdempotencyKeys_CreatedAtUtc` index
  supports an efficient future sweep job.

**Trade-off:** Idempotency keys do not deduplicate submissions where the user intentionally
submits the same transaction twice as two distinct operations (e.g., pays the same amount
to the same account on two separate days). Because the key rotates on success, this is
handled correctly — each successful submission produces a new key. The 24-hour TTL bounds
the deduplication window.

---

## ADR-014 — Standardize on a server-hosted Blazor Web App

**Status:** Implemented
**Files:** `TradePlatform.Api/Components/App.razor`,
`TradePlatform.Api/Components/Routes.razor`,
`TradePlatform.Api/Components/Layout/MainLayout.razor`,
`TradePlatform.Api/Components/Pages/Home.razor`,
`TradePlatform.Api/Endpoints/AuthEndpoints.cs`,
`TradePlatform.Api/Program.cs`,
`docker-compose.yml`,
`docker-compose.test.yml`

**Decision:** Use a single server-hosted Blazor Web App inside `TradePlatform.Api` as the
sole interactive frontend implementation. Browser E2E tests remain in the standalone
`E2E` workspace so the test harness does not depend on a separate UI project.

**Reasoning:** The showcase now carries a single frontend implementation with same-origin
cookie auth and direct access to server-side services. This removes the extra WebAssembly
runtime, client token plumbing, and separate frontend deployment path while preserving the
same browser-visible transaction flow used by the E2E suite.

**Trade-off:** UI rendering now depends on the API host rather than a separately deployable
browser runtime. In exchange, the repo no longer has feature-parity drift or duplicated
frontend infrastructure.

---

## ADR-015 — Do not create incoming projection rows for missing target accounts

**Status:** Implemented
**Files:** `TradePlatform.Api/Handlers/AccountActivityProjectionHandler.cs`,
`TradePlatform.Tests/Api/AccountActivityProjectionHandlerTests.cs`

**Decision:** The account-activity projection creates an incoming row only when the
target account exists. When a transfer fails because the target account is missing, the
projection contains only the source account's outgoing row with the failure reason.

**Reasoning:** Creating an incoming row for a non-existent target account fabricates read
model state for an entity that never legitimately participated in the transaction. The
projection should mirror observable account activity, not merely echo both sides of the
attempted payload.

**Trade-off:** The read model is intentionally asymmetric for this failure case. That
asymmetry is preferable to showing fake inbound activity.
