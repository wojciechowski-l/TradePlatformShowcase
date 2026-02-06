# Trade Platform Showcase

A production-grade distributed trading platform designed to demonstrate **reliable** software architecture patterns. This project moves beyond "Happy Path" prototyping to handle real-world challenges like data inconsistency, partial failures, message duplication, and concurrency.

## Project Intent

This repository is a technology showcase rather than a production-ready trading platform.

It exists to demonstrate:

- **Distributed Messaging Reliability**: Implementing the Transactional Outbox pattern and Dead Letter Queues (DLQ) via **Wolverine**.
- **Idempotency**: Handling duplicate message delivery in distributed background processing.
- **Infrastructure-Backed Testing**: Using Testcontainers for integration tests and ephemeral Docker environments for E2E validation.
- **Observability**: Integration of Prometheus, Grafana, and structured logging (Seq).

## Key Code Highlights

If you are reviewing this repository, the most significant architectural patterns are located in the following files:

- **TradePlatform.Api/Program.cs**
  - Configures Wolverine for the Transactional Outbox and RabbitMQ transport.
  - Demonstrates **Environment Isolation**: Explicitly disables external transports during Integration Tests to prevent deadlocks.

- **TradePlatform.Worker/Program.cs**
  - Implements **Convention-Based Routing**: Automatically routes all messages from the `TradePlatform.Core.DTOs` namespace to the `Notifications` exchange, eliminating manual boilerplate.
  - Configures SQL Server-backed Message Persistence (`DurableInbox`) to ensure idempotency.

- **TradePlatform.Tests/Integration/ApiIntegrationTests.cs**
  - Demonstrates **Slice Testing**: Uses Testcontainers for SQL Server but mocks the Message Broker. This makes tests 3x faster and less brittle than spinning up a full topology.

---

## Key Features & Patterns

### 1. Reliable Messaging (Transactional Outbox)

We utilize **Wolverine** to abstract the Transactional Outbox pattern.

- **Mechanism**: When the API creates a transaction, Wolverine automatically persists the outgoing event to a hidden `wolverine_outbox` table within the same SQL transaction.
- **Benefit**: Guarantees zero lost messages ("At-Least-Once Delivery").

### 2. Scalable Routing

Instead of manually registering every message type, the Worker uses a routing policy:

```csharp
opts.Publish(rules => rules
    .MessagesFromNamespace("TradePlatform.Core.DTOs")
    .ToRabbitExchange(MessagingConstants.NotificationsExchange));
```

This ensures that as the system grows, new notification types work automatically.

### 3. Fault Tolerance
- **Dead Letter Queues (DLQ)**: Poison messages are automatically routed to RabbitMQ DLQs by Wolverine after exhaustively retrying.

- **Resilience**: Configured with durable policies to ensure messages survive broker restarts.

### 4. Architecture
The solution follows a microservices-inspired architecture containerized via Docker Compose:

- **TradePlatform.Api**: .NET 10 REST API. Acts as the gateway, enforcing validation and persisting requests via Wolverine.

- **TradePlatform.Worker**: .NET 10 Host. Consumes messages via Wolverine Handlers and processes trades.

- **Infrastructure**: SQL Server 2022, RabbitMQ, Prometheus, Grafana, Seq.

### 5. Domain Integrity & Type Safety
Beyond infrastructure reliability, the core domain enforces strict correctness:
- **Strongly Typed Domain**: Utilizes **Value Objects** (e.g., `Currency`) and **Enums** instead of primitive strings to prevent logical errors.
- **Referential Integrity**: Database schema strictly enforces Foreign Key relationships between Transactions and Accounts, preventing orphaned records.
- **Defensive Coding**: Entities use `required` properties and validation to ensure no object exists in an invalid state.

----------

## Testing Strategy

We employ a "Testing Pyramid" approach to ensure reliability without flaky mocks.

### 1. Backend Integration Tests (Fail Fast)

Located in `TradePlatform.Tests`, these tests run **before** the full environment spins up.

-   **Tech**: xUnit + **Testcontainers** (MsSql & RabbitMq).
    
-   **Scope**:
    
    -   Verifies atomic database writes.
        
    -   Tests the "Sweeper" recovery logic (simulating expired locks via timeout manipulation).
        
    -   Verifies Worker idempotency and DLQ routing.
        
    -   **No Mocks**: Tests run against real, throwaway container instances.
        

### 2. End-to-End (E2E) Tests

Located in `Client/cypress`, these tests run against the fully deployed Docker Compose environment.

-   **Tech**: Cypress.
    
-   **Scope**: Simulates a real user logging in, placing a trade, and verifying the UI updates after the background worker completes the job.
    

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
    
-   **Messaging**: Wolverine (Transactional Outbox, Durable Inbox) over RabbitMQ
    
-   **Database**: Microsoft SQL Server 2022
    
-   **Testing**: xUnit, Testcontainers, Cypress
    
-   **Observability**: Prometheus, Grafana