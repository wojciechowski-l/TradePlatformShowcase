using Aspire.Hosting;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// --- Parameters & Configuration ---
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var rabbitUser = builder.AddParameter("rabbitmq-user", "guest");
var rabbitPass = builder.AddParameter("rabbitmq-pass", secret: true);

// --- Infrastructure Services ---

// SQL Server 2022
var sqlServer = builder.AddSqlServer("sql-server", password: sqlPassword)
    .WithDataVolume("sql_data")
    .AddDatabase("TradePlatformDb");

// RabbitMQ 4 with Management Plugin (plugins pre-configured by Aspire)
var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: rabbitUser, password: rabbitPass)
    .WithManagementPlugin()
    .WithDataVolume("rabbitmq_data");

// Redis Cache
var redis = builder.AddRedis("redis")
    .WithDataVolume("redis_data");

// Seq Server
var seq = builder.AddSeq("seq")
    .WithDataVolume("seq_data");

// Prometheus & Grafana
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "latest")
    .WithBindMount("../observability/prometheus.yml", "/etc/prometheus/prometheus.yml")
    .WithHttpEndpoint(port: 9090, targetPort: 9090)
    .WaitFor(rabbitmq);

var grafanaAdminPassword = builder.AddParameter("grafana-admin-password", secret: true);
var grafana = builder.AddContainer("grafana", "grafana/grafana", "latest")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", grafanaAdminPassword)
    .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
    .WithVolume("grafana_data", "/var/lib/grafana")
    .WithBindMount("../observability/grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("../observability/grafana/dashboards", "/etc/grafana/dashboards")
    .WithHttpEndpoint(port: 3100, targetPort: 3000)
    .WaitFor(prometheus);

// --- Application Services ---

// Database Migrator Task
var migrator = builder.AddProject<Projects.TradePlatform_Api>("migrator")
    .WithArgs("--migrate-only")
    .WithReference(sqlServer)
    .WithReference(seq)
    .WaitFor(sqlServer)
    .WaitFor(seq);

// Trade Platform API
var api = builder.AddProject<Projects.TradePlatform_Api>("api")
    .WithReference(sqlServer)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WithReference(seq)
    .WaitFor(sqlServer)
    .WaitFor(rabbitmq)
    .WaitFor(redis)
    .WaitFor(seq)
    .WaitForCompletion(migrator);

// Trade Platform Worker
var worker = builder.AddProject<Projects.TradePlatform_Worker>("worker")
    .WithReference(sqlServer)
    .WithReference(rabbitmq)
    .WithReference(seq)
    .WaitFor(sqlServer)
    .WaitFor(rabbitmq)
    .WaitFor(seq)
    .WaitForCompletion(migrator);

// --- E2E Playwright Testing Profile ---
if (builder.Configuration.GetValue<bool>("E2E_TESTING"))
{
    builder.AddContainer("playwright-e2e", "mcr.microsoft.com/playwright", "v1.40.0-jammy")
        .WithEnvironment("BASE_URL", api.GetEndpoint("http"))
        .WithEnvironment("API_URL", api.GetEndpoint("http"))
        .WithEnvironment("CI", "true")
        .WithArgs("npx", "playwright", "test", "--fail-on-flaky-tests")
        .WithBindMount("../E2E/test-results", "/app/test-results")
        .WithBindMount("../E2E/playwright-report", "/app/playwright-report")
        .WaitFor(api)
        .WaitFor(worker);
}

builder.Build().Run();