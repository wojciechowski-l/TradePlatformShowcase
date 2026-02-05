# Trade Platform Showcase

A production-grade distributed trading platform designed to demonstrate **reliable** software architecture patterns. This project moves beyond "Happy Path" prototyping to handle real-world distributed system challenges like partial failures, message duplication, and concurrency.

## Project Intent

This repository is a technology showcase rather than a production-ready trading platform.

It exists to demonstrate:

-   **Distributed Messaging Reliability**: Implementing the Transactional Outbox pattern and robust consumer patterns via MassTransit.
-   **Idempotency**: Handling duplicate message delivery in distributed background processing.
-   **Infrastructure-Backed Testing**: Using Testcontainers for integration tests and ephemeral Docker environments for E2E validation.
-   **Observability**: Integration of Prometheus, Grafana, and structured logging (Seq).

Features are added opportunistically to support these architectural goals; the project is not considered "feature complete" regarding business logic.

## Key Code Highlights

If you are reviewing this repository, the most significant architectural patterns are located in the following files:

-   **TradePlatform.Infrastructure/Messaging/OutboxPublisherWorker.cs**
    -   Implements the Transactional Outbox pattern, ensuring atomic database writes are eventually consistent with the message broker.
    -   Demonstrates **MassTransit** integration for publishing.

-   **TradePlatform.Worker/Consumers/TransactionCreatedConsumer.cs**
    -   Contains the idempotent message handling logic.
    -   Demonstrates how to decouple business logic from messaging transport.

-   **TradePlatform.Api/Infrastructure/Consumers/NotificationConsumer.cs**
    -   Demonstrates bridging asynchronous messaging (RabbitMQ) with real-time WebSocket clients (SignalR).

-   **run-e2e-tests.ps1**
    -   An orchestration script that manages the full test lifecycle: running backend integration tests, spinning up the Docker environment, and executing Cypress E2E tests.

----------

## Key Features & Patterns

### 1. Reliable Messaging (Transactional Outbox)

Instead of publishing to RabbitMQ directly (which risks data inconsistency if the broker is down), the API saves a `TransactionRecord` and an `OutboxMessage` to SQL Server in a **single atomic transaction**.

-   **Benefit**: Guarantees zero lost messages ("At-Least-Once Delivery").
-   **Concurrency**: Uses SQL Server `UPDLOCK` and `READPAST` hints to ensure safe, concurrent processing without race conditions.
-   **Recovery**: A background "Sweeper" process detects messages stuck in `InFlight` status (exceeding a configured timeout) and automatically resets them to `Pending`.

### 2. Idempotent Consumer

The Worker handles the "At-Least-Once" delivery guarantee by ensuring processing is idempotent.

-   **Logic**: Uses atomic SQL updates (`UPDATE ... WHERE Status = 'Pending'`) to lock and process transactions.
-   **Benefit**: Processing the same message twice (e.g., due to a network retry) never results in corrupt data or double spending.

### 3. Performance Optimizations

-   **Fat Event Pattern**: The `OutboxMessage` payload contains the full event state (`TransactionCreatedEvent`), allowing the Worker to process notifications without querying the database (Read Reduction).
-   **Zero-Latency Authorization**: SignalR connections validate account ownership using **JWT Claims** (`urn:tradeplatform:accountid`) embedded in the access token.

### 4. Fault Tolerance

-   **Dead Letter Queues (DLQ)**: MassTransit automatically moves failed messages to `_error` queues after exhausting retry policies.
-   **Resilience**: Configured with automatic retries and exponential backoff.

----------

## Architecture

The solution follows a microservices-inspired architecture containerized via Docker Compose:

-   **Client (Frontend)**: React 19 + Vite application (Material UI).
-   **TradePlatform.Api**: .NET 10 REST API. Write-model gateway.
-   **TradePlatform.Worker**: Background service consuming messages via MassTransit.
-   **Infrastructure**: SQL Server, RabbitMQ, Prometheus, Grafana.

----------

## Testing Strategy

### 1. Backend Integration Tests (Fail Fast)

Located in `TradePlatform.Tests`, these tests run **before** the full environment spins up.

-   **Tech**: xUnit + **Testcontainers** (MsSql) + **MassTransit Test Harness**.
-   **Scope**:
    -   Verifies atomic database writes.
    -   Tests the "Sweeper" recovery logic.
    -   Verifies Consumer idempotency using in-memory messaging harness.

### 2. End-to-End (E2E) Tests

Located in `Client/cypress`, these tests run against the fully deployed Docker Compose environment.

-   **Tech**: Cypress.
-   **Scope**: Simulates a real user logging in, placing a trade, and verifying the UI updates.

----------

## Getting Started

### Prerequisites

-   **Docker Desktop**
-   **.NET 10 SDK**
-   **PowerShell**

### 1. Run the Full Test Suite

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
    
-   **Messaging**: RabbitMQ (MassTransit style patterns implemented manually)
    
-   **Database**: Microsoft SQL Server 2022
    
-   **Testing**: xUnit, Testcontainers, Cypress
    
-   **Observability**: Prometheus, Grafana
