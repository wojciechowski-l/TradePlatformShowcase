using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using TradePlatform.Infrastructure.Data;

namespace TradePlatform.Tests;

public sealed class SqlServerTestDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContainer.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public async Task<TradeContext> CreateContextAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            InitialCatalog = $"TradePlatformTests_{Guid.NewGuid():N}"
        };

        var options = new DbContextOptionsBuilder<TradeContext>()
            .UseSqlServer(connectionStringBuilder.ConnectionString)
            .Options;

        var context = new TradeContext(options);
        await context.Database.MigrateAsync(cancellationToken);

        return context;
    }
}
