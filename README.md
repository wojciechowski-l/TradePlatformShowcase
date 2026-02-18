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

---

## Key Code Highlights

If you are reviewing this repository, the most significant architectural patterns are located in the following files:

- **`TradePlatform.Infrastructure/Services/RebusSqlTransactionScopeManager.cs`**
  The core of the reliability story. Implements the Outbox pattern by binding a Rebus `RebusTransactionScope` to the same ADO.NET transaction as the EF Core `SaveChangesAsync` call. A message is only published if the database commit succeeds — there is no window where a record is written but no message is sent, or a message is sent for a transaction that was rolled back.

- **`TradePlatform.Api/Hubs/TradeHub.cs`** and **`TradePlatform.Infrastructure/Services/DbAccountOwnershipService.cs`**
  Together these implement the ownership model. `TradeHub` validates that a SignalR client can only join the group for an account they own. `DbAccountOwnershipService` first checks a custom JWT claim (zero-cost fast path), then falls back to a database query with a short-lived memory cache to absorb reconnect bursts.

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

- **Known gap**: `TransactionStatus.Failed` is defined in the domain enum but is not yet activated. After 3 failed delivery attempts, Rebus dead-letters the message and the transaction record remains `Pending`. The distinction between a dead-lettered message (infrastructure failure) and an application-level failure (e.g. insufficient funds) is a deliberate open design question — resolving it requires defining what `Failed` means in business terms before wiring up the status transition.

### 3. Ownership-Enforced Boundaries

The system prevents any authenticated user from acting on accounts they do not own. The check is applied at two independent boundaries:

- **HTTP write path** (`TransactionsController`): `IAccountOwnershipService.IsOwnerAsync` is called before the transaction service is invoked. Returns `403 Forbidden` if the caller does not own `SourceAccountId`.
- **SignalR group join** (`TradeHub`): `IsOwnerAsync` is called before adding a connection to an account's notification group. Throws `HubException` if the check fails.

`DbAccountOwnershipService` resolves ownership in two steps:
1. Checks the `urn:tradeplatform:accountid` claim embedded in the JWT at login time by `TradeUserClaimsPrincipalFactory` — zero DB cost for the common case.
2. Falls back to a database query (with a 30-second `IMemoryCache` TTL) when the claim is absent or does not match — protects against thundering-herd reconnects after a service restart.

### 4. Scalable Real-Time Notifications (SignalR + Redis)

The Worker publishes a `TransactionProcessedEvent` after processing. The API subscribes to this event and pushes a `ReceiveStatusUpdate` message to the relevant SignalR group.

- **Redis Backplane**: Ensures SignalR messages reach the correct client regardless of which API replica they are connected to, enabling horizontal scaling of the API layer.
- **Event-Driven**: The Worker has no direct dependency on the API's internal topology — it only publishes an event.

### 5. Explicit Routing

Instead of relying on opaque naming conventions, all message-to-queue mappings are declared explicitly in code. Every message type's destination is traceable from a single location in each project's `Program.cs`.

### 6. Observability

A complete three-pillar observability stack:

- **Distributed Tracing**: OpenTelemetry generates a `TraceId` at the API entry point. Rebus propagates it through RabbitMQ to the Worker, allowing the full request flow across both services to be visualised in Seq.
- **Metrics**: Runtime and business metrics (trade volume, trades/sec, queue depth, CPU, GC pressure) exposed via Prometheus scrape endpoints.
- **Visualisation**: Grafana is configured via provisioning-as-code — dashboards load automatically on startup with no manual setup.

### 7. Domain Integrity & Type Safety

- **Value Objects**: `Currency` is a strongly-typed value object rather than a raw string, enforcing valid ISO format at the boundary of the domain.
- **Referential Integrity**: Database schema enforces foreign key relationships between `TransactionRecord` and `Account`.
- **Validation**: FluentValidation runs as an MVC filter, rejecting structurally invalid requests before they reach the controller body.

---

## Architecture

The solution is a distributed system composed of two .NET 10 services, a React frontend, and supporting infrastructure, orchestrated via Docker Compose:

- **TradePlatform.Api** — REST API and SignalR hub. Validates requests, enforces ownership, and dispatches commands via the Rebus Outbox.
- **TradePlatform.Worker** — Background host. Consumes messages from the `trade-orders` queue, processes transactions, and publishes status events.
- **Client** — React 19 + TypeScript + Vite + Material UI frontend.
- **Infrastructure** — SQL Server 2022, RabbitMQ, Redis, Prometheus, Grafana, Seq.

---

## Testing Strategy

A testing pyramid with two layers: backend integration tests that run against real infrastructure containers, and full E2E tests that run against the deployed Docker Compose environment.

### 1. Backend Integration & Unit Tests

Located in `TradePlatform.Tests`.

**Tech:** xUnit, Testcontainers (MsSql & RabbitMQ), Moq

**Scope:**
- Unit tests cover `TransactionService`, `TransactionCreatedHandler`, and `TransactionDtoValidator` in isolation using mocks for infrastructure dependencies.
- Integration tests run against real, ephemeral SQL Server and RabbitMQ containers. Authentication is replaced with a `TestAuthHandler` that injects per-request identity via HTTP headers, replicating the claims structure of the real JWT — the ownership checks, validator, and full Rebus pipeline run unmodified.

### 2. End-to-End (E2E) Tests

Located in `Client/e2e`.

**Tech:** Playwright

**Scope:**
Simulates a real user registering, logging in, placing a trade, and verifying the UI updates via SignalR after the Worker processes the transaction. Includes both happy-path and failure scenarios.

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

```powershell
docker-compose up -d --build
```

| Service | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API (Scalar docs) | http://localhost:8080/scalar/v1 |
| RabbitMQ Admin | http://localhost:15672 (guest / guest) |
| Grafana | http://localhost:3100 |
| Seq (Logs) | http://localhost:5341 |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, C#, Entity Framework Core 10 |
| Frontend | React 19, TypeScript, Vite, Material UI |
| Messaging | Rebus 8 over RabbitMQ |
| Database | SQL Server 2022 |
| Real-time | ASP.NET Core SignalR with Redis backplane |
| Testing | xUnit, Testcontainers, Playwright |
| Observability | OpenTelemetry, Prometheus, Grafana, Seq |