# Trade Platform Showcase

A distributed trading platform designed to demonstrate **production-pattern** software architecture. This project moves beyond "Happy Path" prototyping to handle real-world challenges like distributed messaging, atomic outbox writes, concurrency, and environment isolation.

## Project Intent

This repository is a technology showcase rather than a production-ready trading platform.

It exists to demonstrate:

- **Distributed Messaging**: Reliable message transport using **Rebus** over RabbitMQ with explicit, type-based routing.
- **Atomic Outbox**: Guaranteeing that a database write and a message publish either both succeed or both fail, with no possibility of a phantom message.
- **Ownership-Enforced API**: An authorisation model that prevents users from transacting on accounts they do not own, enforced at both the HTTP and SignalR boundaries.
- **Infrastructure-Backed Testing**: Using Testcontainers for integration tests (SQL Server & RabbitMQ) and a full Docker Compose environment for E2E validation.
- **Observability**: OpenTelemetry tracing across service boundaries, Prometheus metrics, Grafana dashboards, and structured logging via Seq.
- **CQRS-lite Projection**: A read-side account activity feed maintained asynchronously from bus events, with a rebuild path for replaying the projection from the write-side source of truth.

---

## Architecture Documentation

- Architecture.md — runtime topology, request lifecycle, identity model, scaling
- Decisions.md — architecture decision records (ADR-001 through ADR-015)
- FailureModes.md — failure analysis, recovery behaviour, known gaps

---

## Key Code Highlights

If you are reviewing this repository, the most significant architectural patterns are located in the following files:

- **`TradePlatform.Infrastructure/Services/RebusSqlTransactionScopeManager.cs`**
  The core of the reliability story. Implements the Outbox pattern by binding a Rebus `RebusTransactionScope` to the same ADO.NET transaction as the EF Core `SaveChangesAsync` call. A message is only published if the database commit succeeds — there is no window where a record is written but no message is sent, or a message is sent for a transaction that was rolled back.

- **`TradePlatform.Api/Hubs/TradeHub.cs`** and **`TradePlatform.Infrastructure/Services/DbAccountOwnershipService.cs`**
  Together these implement the ownership model. `TradeHub` validates that a SignalR client can only join the group for an account they own. `DbAccountOwnershipService` first checks a custom authenticated-user claim (zero-cost fast path), then falls back to a database query with a short-lived memory cache to absorb reconnect bursts.

- **`TradePlatform.Api/Controllers/TransactionsController.cs`**
  The HTTP write boundary. Enforces account ownership before dispatching to the service layer — the authenticated user's identity is validated against the `SourceAccountId` in the request body, preventing IDOR.

- **`TradePlatform.Api/Program.cs`**
  Configures Rebus for message transport with explicit queue mapping via `TypeBased` routing, the Outbox store, and retry policy.

- **`TradePlatform.Worker/Program.cs`**
  Configures the Rebus worker to consume from the `trade-orders` queue with a Simple Retry Strategy.

- **`TradePlatform.Tests/Integration/ApiIntegrationTests.cs`**
  Full topology integration testing. Uses Testcontainers to spin up both SQL Server and RabbitMQ instances, and manually declares queues via `RabbitMQ.Client` to simulate the full runtime topology before any test runs.

---

## Key Features & Patterns

### 1. Atomic Outbox (Rebus + SQL Server)

The most critical reliability guarantee in the system. Implemented in `RebusSqlTransactionScopeManager`:

```csharp
using var transaction  = await dbContext.Database.BeginTransactionAsync();
using var rebusScope   = new RebusTransactionScope();

rebusScope.UseOutbox((SqlConnection)dbConnection, (SqlTransaction)dbTransaction);

await action();

await rebusScope.CompleteAsync();
await transaction.CommitAsync();
```

The `RebusTransactionScope` writes outbound messages to the `RebusOutbox` table inside the same SQL transaction. A background Rebus process forwards them to RabbitMQ only after the commit succeeds. If `CommitAsync` throws, neither the record nor the message exists. This eliminates the dual-write problem without a distributed transaction coordinator.

### 2. Reliable Messaging (Rebus over RabbitMQ)

- **Mechanism**: The API sends commands (`TransactionCreatedEvent`) via `bus.Send()`, routed to specific queues via explicit Type-Based routing:

```csharp
.Routing(r => r.TypeBased().Map<TransactionCreatedEvent>(MessagingConstants.OrdersQueue))
```

- **Fault Tolerance**: Configured with a `SimpleRetryStrategy` (3 attempts) before dead-lettering. Queues are durable to survive broker restarts.
- **Consumer idempotency**: Rebus consumers use a durable SQL inbox keyed by message id so duplicate deliveries are discarded before side effects are re-applied.
- **Concurrency control**: The Worker locks the transaction row and both account rows while applying the transfer, preventing competing consumers from double-mutating balances.

- **Business lifecycle**: The Worker now drives `Pending -> Validated -> Processing -> Processed/Failed`. Missing target accounts, currency mismatches, and insufficient funds end in `Failed` with a reason. Dead-lettered infrastructure failures are still a separate concern and can leave a transaction pre-terminal for manual recovery.

### 3. Ownership-Enforced Boundaries

The system prevents any authenticated user from acting on accounts they do not own. The check is applied at two independent boundaries:

- **HTTP write path** (`TransactionsController`): `IAccountOwnershipService.IsOwnerAsync` is called before the transaction service is invoked. Returns `403 Forbidden` if the caller does not own `SourceAccountId`.
- **SignalR group join** (`TradeHub`): `IsOwnerAsync` is called before adding a connection to an account's notification group. Throws `HubException` if the check fails.

`DbAccountOwnershipService` resolves ownership in two steps:
1. Checks the `urn:tradeplatform:accountid` claim added to the signed-in principal by `TradeUserClaimsPrincipalFactory` — zero DB cost for the common case.
2. Falls back to a database query (with a 30-second `IMemoryCache` TTL) when the claim is absent or does not match — protects against thundering-herd reconnects after a service restart.

### 4. Scalable Real-Time Notifications (SignalR + Redis)

The Worker publishes `TransactionStatusChangedEvent` messages for each lifecycle transition. The API subscribes to these events and pushes a `ReceiveStatusUpdate` message to the relevant SignalR group for clients that use `TradeHub`.

- **Redis Backplane**: Ensures SignalR messages reach the correct client regardless of which API replica they are connected to, enabling horizontal scaling of the API layer.
- **Event-Driven**: The Worker has no direct dependency on the API's internal topology — it only publishes an event.
- **Stable event identity**: SignalR payloads now include a stable `EventId` derived from the Rebus message id so clients can deduplicate realtime updates if needed.

### 5. Explicit Routing

Instead of relying on opaque naming conventions, all message-to-queue mappings are declared explicitly in code. Every message type's destination is traceable from a single location in each project's `Program.cs`.

### 6. Observability

A complete three-pillar observability stack:

- **Distributed Tracing**: OpenTelemetry generates a `TraceId` at the API entry point. Rebus propagates it through RabbitMQ to the Worker, allowing the full request flow across both services to be visualised in Seq.
- **Metrics**: Runtime and business metrics (trade volume, trades/sec, queue depth, CPU, GC pressure) exposed via Prometheus scrape endpoints.
- **Visualisation**: Grafana is configured via provisioning-as-code — dashboards load automatically on startup with no manual setup.

### 7. CQRS-lite Read Model

The API subscribes to `TransactionSubmittedEvent` and `TransactionStatusChangedEvent` and projects them into a dedicated `AccountActivityProjections` table. The hosted Blazor UI reads this projection server-side, which makes eventual consistency visible: a transaction is accepted first, then appears in the feed as the projection catches up, then transitions through `Validated` / `Processing` to its terminal status.

- **Projection idempotency**: the projection consumer uses the same durable inbox pattern for duplicate Rebus deliveries, and lower-order lifecycle events are prevented from regressing the read model.
- **Projection integrity**: incoming rows are only created when the target account exists, so a failed transfer to a missing target does not fabricate inbound activity for a non-existent account.
- **Replay story**: in development/test, `POST /api/maintenance/projections/account-activity/rebuild` rebuilds the read model from `Transactions`. A convenience script is included at `./rebuild-account-activity-projection.ps1`.

### 8. Domain Integrity & Type Safety

- **Value Objects**: `Currency` is a strongly-typed value object rather than a raw string, enforcing valid ISO format at the boundary of the domain.
- **Referential Integrity**: Database schema enforces foreign key relationships between `TransactionRecord` and `Account`.
- **Showcase usability**: Newly provisioned accounts are seeded with a fixed starting balance of `1000`, so a first-time user can immediately exercise the async transaction lifecycle.
- **Validation**: FluentValidation runs as an MVC filter, rejecting structurally invalid requests before they reach the controller body.

---

## Architecture

The solution is a distributed system composed of two .NET 10 services, a server-hosted Blazor Web App, and supporting infrastructure, orchestrated via Docker Compose:

- **TradePlatform.Api** — REST API, SignalR hub, and hosted Blazor UI. Validates requests, enforces ownership, and dispatches commands via the Rebus Outbox.
- **TradePlatform.Worker** — Background host. Consumes messages from the `trade-orders` queue, processes transactions, and publishes status events.
- **E2E** — Standalone Playwright workspace for browser tests, decoupled from the frontend implementation.
- **Infrastructure** — SQL Server 2022, RabbitMQ, Redis, Prometheus, Grafana, Seq.

---

## Testing Strategy

A testing pyramid with two layers: backend integration tests that run against real infrastructure containers, and full E2E tests that run against the deployed Docker Compose environment.

### 1. Backend Integration & Unit Tests

Located in `TradePlatform.Tests`.

**Tech:** xUnit, Testcontainers (MsSql & RabbitMQ), Moq

**Scope:**
- Unit tests cover `TransactionService`, `TransactionCreatedHandler`, and `TransactionDtoValidator` in isolation using mocks for infrastructure dependencies.
- Integration tests run against real, ephemeral SQL Server and RabbitMQ containers. Authentication is replaced with a `TestAuthHandler` that injects per-request identity via HTTP headers, replicating the claims structure of the real authenticated principal — the ownership checks, validator, and full Rebus pipeline run unmodified.

### 2. End-to-End (E2E) Tests

Located in `E2E/e2e`.

**Tech:** Playwright

**Scope:**
Simulates a real user registering, logging in, placing a trade, and verifying the UI settles on the correct terminal state through the asynchronous read model. Includes both happy-path and failure scenarios. The E2E harness waits for both the API and Worker to be ready before Playwright starts, reducing cold-start flakiness in the async pipeline.

---

## Getting Started

### Prerequisites

- **Docker Desktop**
- **.NET 10 SDK** (for local development and migrations)
- **PowerShell** (for automation scripts)

### 1. Run the Full Test Suite (Recommended)

Runs the backend integration tests first. If they pass, spins up the Docker Compose environment and runs the Playwright E2E tests.

```powershell
./run-e2e-tests.ps1
```

### 2. Start the Application Manually

Copy the environment file and fill in your values:
```powershell
cp .env.example .env
```

Then start the stack:
```powershell
docker compose up -d --build
```

| Service | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API (Scalar docs) | http://localhost:3000/scalar/v1 |
| RabbitMQ Admin | http://localhost:15672 (guest / guest) |
| Grafana | http://localhost:3100 |
| Seq (Logs) | http://localhost:5341 |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, C#, Entity Framework Core 10 |
| Frontend | Blazor Web App |
| Messaging | Rebus 8 over RabbitMQ |
| Database | SQL Server 2022 |
| Real-time | ASP.NET Core SignalR with Redis backplane |
| Testing | xUnit, Testcontainers, Playwright |
| Observability | OpenTelemetry, Prometheus, Grafana, Seq |
