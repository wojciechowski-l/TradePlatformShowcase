# Trade Platform Showcase

A production-grade distributed trading platform designed to demonstrate **reliable** software architecture patterns. This project moves beyond "Happy Path" prototyping to handle real-world challenges like distributed messaging, concurrency, and environment isolation.

## Project Intent

This repository is a technology showcase rather than a production-ready trading platform.

It exists to demonstrate:

- **Distributed Messaging**: Implementing reliable message transport using **Rebus** over RabbitMQ.
- **Explicit Routing**: Moving away from "magic" conventions to explicit, Type-Based routing for clarity.
- **Infrastructure-Backed Testing**: Using Testcontainers for integration tests (SQL Server & RabbitMQ) and ephemeral Docker environments for E2E validation.
- **Observability**: Integration of Prometheus, Grafana, and structured logging (Seq).

## Key Code Highlights

If you are reviewing this repository, the most significant architectural patterns are located in the following files:

- **TradePlatform.Api/Program.cs**
  - Configures **Rebus** for message transport.
  - Demonstrates explicit queue mapping via `TypeBased` routing.

- **TradePlatform.Worker/Program.cs**
  - Configures the Rebus worker to consume from the `trade-orders` queue.
  - Sets up retry policies (Simple Retry Strategy) to handle transient failures.

- **TradePlatform.Tests/Integration/ApiIntegrationTests.cs**
  - Demonstrates **Full Topology Testing**: Uses Testcontainers to spin up both SQL Server and RabbitMQ.
  - Shows how to manually manipulate infrastructure (declaring queues via `RabbitMQ.Client`) to simulate microservice dependencies during testing.

---

## Key Features & Patterns

### 1. Reliable Messaging (Rebus)

We utilize **Rebus** to abstract the messaging infrastructure.

- **Mechanism**: The API sends commands (`TransactionCreatedEvent`) via `bus.Send()`, which are routed to specific queues.
- **Benefit**: Decouples the API from the Worker service, allowing for independent scaling and maintenance.

### 2. Explicit Routing

Instead of relying on opaque conventions, the application uses explicit Type-Based routing:

```csharp
.Routing(r => r.TypeBased().Map<TransactionCreatedEvent>(MessagingConstants.OrdersQueue))
```

This ensures that developers can easily trace where a specific message type is being delivered.

## 3. Fault Tolerance

- **Retries:** Configured with a `SimpleRetryStrategy` (3 attempts) to handle transient errors before giving up.

- **Resilience:** RabbitMQ transport is configured with durable queues to ensure messages survive broker restarts.

---

## 4. Observability & Monitoring
The platform implements a complete observability pillar to prevent "black box" operations:

- **Distributed Tracing**: Uses **OpenTelemetry** to generate a unique `TraceId` at the API, which Rebus propagates through RabbitMQ to the Worker. This allows visualizing the entire request flow across microservices in Seq.

- **Metrics**: Exposes runtime and business metrics (e.g., Queue Depth, CPU, GC) via Prometheus endpoints.

- **Visualisation**: **Grafana** is configured via "Provisioning as Code" to automatically load dashboards on startup, visualizing system health without manual setup.

## 5. Architecture

The solution follows a microservices-inspired architecture containerized via **Docker Compose**:

- **TradePlatform.Api:**  
  .NET 10 REST API. Acts as the gateway, enforcing validation and dispatching commands via Rebus.

- **TradePlatform.Worker:**  
  .NET 10 Host. Consumes messages via Rebus Handlers (`IHandleMessages<T>`) and processes trades.

- **Infrastructure:**  
  SQL Server 2022, RabbitMQ, Prometheus, Grafana, Seq.

---

## 5. Domain Integrity & Type Safety

Beyond infrastructure reliability, the core domain enforces strict correctness:

- **Strongly Typed Domain:**  
  Utilizes Value Objects (e.g., `Currency`) and Enums instead of primitive strings to prevent logical errors.

- **Referential Integrity:**  
  Database schema strictly enforces Foreign Key relationships between Transactions and Accounts.

- **Defensive Coding:**  
  Entities use required properties and validation to ensure no object exists in an invalid state.

---

# Testing Strategy

We employ a **"Testing Pyramid"** approach to ensure reliability without flaky mocks.

## 1. Backend Integration Tests

Located in `TradePlatform.Tests`, these tests run before the full environment spins up.

**Tech:** xUnit + Testcontainers (MsSql & RabbitMQ)

**Scope:**

- Verifies atomic database writes.
- Tests the messaging pipeline by simulating queue infrastructure.
- **No Mocks:** Tests run against real, throwaway container instances for maximum confidence.

---

## 2. End-to-End (E2E) Tests

Located in `Client/cypress`, these tests run against the fully deployed Docker Compose environment.

**Tech:** Cypress

**Scope:**  
Simulates a real user logging in, placing a trade, and verifying the UI updates after the background worker completes the job via SignalR.

    

----------

## Getting Started

### Prerequisites

-   **Docker Desktop**
    
-   **.NET 10 SDK** (For local development/migrations)
    
-   **PowerShell** (For automation scripts)

### 1. Run the Full Test Suite (Recommended)

This script runs the Backend Integration tests first. If they pass, it spins up the Docker environment and runs the Cypress E2E tests.

PowerShell

```
./Review/run-e2e-tests.ps1

```

### 2. Start the Application Manually

If you just want to run the app:

PowerShell

```
docker-compose up -d --build

```

-   Frontend: http://localhost:3000
    
-   API Documentation (Scalar): http://localhost:8080/scalar/v1
    
-   RabbitMQ Admin: http://localhost:15672 (guest/guest)
    
-   Grafana: http://localhost:3100
    
-   Seq (Logs): http://localhost:5341
    

### Tech Stack

-   **Backend**: .NET 10, C#, Entity Framework Core
    
-   **Frontend**: React 19, TypeScript, Vite, Material UI
    
-   **Messaging**: Rebus over RabbitMQ
    
-   **Database**: Microsoft SQL Server 2022
    
-   **Testing**: xUnit, Testcontainers, Cypress
    
-   **Observability**: Prometheus, Grafana