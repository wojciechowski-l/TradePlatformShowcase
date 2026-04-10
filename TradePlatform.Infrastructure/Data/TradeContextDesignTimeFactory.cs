using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradePlatform.Infrastructure.Data
{
    public sealed class TradeContextDesignTimeFactory : IDesignTimeDbContextFactory<TradeContext>
    {
        public TradeContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? ReadConnectionStringFromAppSettings()
                ?? throw new InvalidOperationException(
                    "Could not resolve the DefaultConnection string for design-time TradeContext creation.");

            var options = new DbContextOptionsBuilder<TradeContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new TradeContext(options);
        }

        private static string? ReadConnectionStringFromAppSettings()
        {
            foreach (var path in GetCandidateAppSettingsPaths())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
                    connectionStrings.TryGetProperty("DefaultConnection", out var defaultConnection))
                {
                    return defaultConnection.GetString();
                }
            }

            return null;
        }

        private static IEnumerable<string> GetCandidateAppSettingsPaths()
        {
            var currentDirectory = Directory.GetCurrentDirectory();

            yield return Path.Combine(currentDirectory, "TradePlatform.Api", "appsettings.json");
            yield return Path.Combine(currentDirectory, "..", "TradePlatform.Api", "appsettings.json");
            yield return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        }
    }
}
