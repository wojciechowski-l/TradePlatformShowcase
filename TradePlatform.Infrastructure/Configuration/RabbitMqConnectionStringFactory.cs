using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TradePlatform.Infrastructure.Configuration;

public static class RabbitMqConnectionStringFactory
{
    public static string Create(IConfiguration configuration)
    {
        var explicitConnectionString = configuration["RabbitMQ:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var username = configuration["RabbitMQ:Username"] ?? "guest";
        var password = configuration["RabbitMQ:Password"] ?? "guest";
        var host = configuration["RabbitMQ:Host"] ?? "localhost";
        var port = configuration["RabbitMQ:Port"] ?? "5672";

        return string.Format(
            CultureInfo.InvariantCulture,
            "amqp://{0}:{1}@{2}:{3}",
            Uri.EscapeDataString(username),
            Uri.EscapeDataString(password),
            host,
            port);
    }
}
