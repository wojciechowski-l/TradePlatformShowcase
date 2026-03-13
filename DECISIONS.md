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

## ADR-003 — Explicit TypeBased routing; pub/sub Publish for worker events

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs`, `TradePlatform.Worker/Program.cs`,
`TradePlatform.Core/Constants/MessagingConstants.cs`

**Decision:** Point-to-point commands (`TransactionCreatedEvent`) use TypeBased routing
with explicit queue mapping declared in `Program.cs`. Worker-originated events
(`TransactionProcessedEvent`) use `bus.Publish()` with the API subscribing via
`bus.Subscribe<TransactionProcessedEvent>()` at startup. The Worker's routing table
contains no entry for `TransactionProcessedEvent` because `Publish` uses the RabbitMQ
topic exchange, not the TypeBased send table.

This separation means:
- API → Worker: point-to-point Send, TypeBased, explicit queue target.
- Worker → API: pub/sub Publish, exchange-based, subscriber-driven.

**Reasoning:** `TransactionCreatedEvent` is a command with a single known consumer —
point-to-point Send is the correct Rebus primitive. `TransactionProcessedEvent` is a
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
only reaches clients connected to the same API process. `TransactionProcessedEvent`
arrives at an arbitrary API replica via RabbitMQ. That replica must be able to forward the
SignalR push to whichever replica holds the WebSocket. Redis pub/sub provides this
cross-replica fan-out.

**Trade-off:** Adds a Redis dependency to the API. Redis is already present in the stack
for this purpose. The channel prefix isolates this application's backplane traffic if
Redis is shared.

---

## ADR-005 — Two-step ownership check (JWT claim → DB with cache)

**Status:** Implemented
**Files:** `TradePlatform.Infrastructure/Services/DbAccountOwnershipService.cs`,
`TradePlatform.Api/Infrastructure/TradeUserClaimsPrincipalFactory.cs`

**Decision:** `DbAccountOwnershipService.IsOwnerAsync` first compares the
`urn:tradeplatform:accountid` JWT claim to the requested account ID. On a miss, it falls
back to a database query. Positive DB results are cached in `IMemoryCache` for 30 seconds.
Negative results are never cached.

**Reasoning:** The ownership check is on the hot path for every trade submission and every
SignalR group join. A pure DB query per request does not scale. The JWT claim handles the
common case at zero cost. The 30-second cache absorbs reconnect storms after a service
restart without requiring distributed cache coordination. Negative results are not cached
to prevent a transient denial from persisting across a subsequent legitimate ownership
grant.

**Trade-off:** The claim is embedded at login time. If account ownership changes between
token issuance and expiry, the claim will be stale until the next login. The cache is
process-local; under horizontal API scale, each replica caches independently, creating
bounded per-replica inconsistency windows within the TTL.

---

## ADR-006 — ASP.NET Identity API endpoints for authentication

**Status:** Implemented
**Files:** `TradePlatform.Api/Program.cs` (`MapIdentityApi<ApplicationUser>()`)

**Decision:** Use the built-in `AddIdentityApiEndpoints` + `MapIdentityApi` to expose
registration and login endpoints, rather than building custom auth controllers.

**Reasoning:** Authentication implementation is not a differentiating concern of this
project. Identity API endpoints provide a complete, standards-conformant JWT-issuing
surface with minimal code, leaving focus on the messaging, reliability, and ownership
patterns that are the project's actual scope.

**Trade-off:** The endpoint contract is dictated by ASP.NET Identity and is not
customisable beyond the claims factory extension point (`TradeUserClaimsPrincipalFactory`).
Accepted for this project's scope.

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

## ADR-010 — TransactionStatus.Failed deliberately not activated

**Status:** Known gap — open design question
**Files:** `TradePlatform.Core/Entities/TransactionRecord.cs` (enum definition),
`TradePlatform.Worker/Handlers/TransactionCreatedHandler.cs`

**Decision:** The `Failed` status value exists in the domain enum but is not wired to
any state transition. Dead-lettered messages leave `TransactionRecord` in `Pending`
indefinitely.

**Reasoning:** Two distinct failure modes have not been distinguished:

1. **Infrastructure failure** — Rebus exhausts 3 delivery attempts and dead-letters the
   message. The transaction record never transitions out of `Pending`.
2. **Application failure** — The transaction is structurally valid but cannot be processed
   (e.g., insufficient funds). This requires a defined business rule before a `Failed`
   transition is meaningful.

Activating `Failed` without distinguishing these two modes would conflate infrastructure
faults with application-level rejections. The decision to leave it unactivated is
deliberate and recorded here to prevent it from being treated as an oversight.

**Resolution path:** Define the business semantics of `Failed`. Add a dead-letter consumer
or application-level exception handler in the Worker. Wire the status transition and emit
a `TransactionProcessedEvent` with `Status = Failed` so the client receives a definitive
terminal state.

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
