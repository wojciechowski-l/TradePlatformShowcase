using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradePlatform.BlazorClient.Models;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginResponse
{
    public string TokenType { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class TransactionRequest
{
    public string SourceAccountId { get; set; } = string.Empty;
    public string TargetAccountId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public sealed class TransactionResponse
{
    public Guid Id { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionStatus Status { get; set; }
}

public sealed class AccountDto
{
    public string Id { get; set; } = string.Empty;

    [JsonConverter(typeof(CurrencyStringJsonConverter))]
    public string Currency { get; set; } = string.Empty;

    public string OwnerId { get; set; } = string.Empty;
    public decimal? Balance { get; set; }
}

public sealed class AccountActivityDto
{
    public Guid TransactionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string CounterpartyAccountId { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AccountActivityDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime LastEventUtc { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class TransactionUpdate
{
    public Guid TransactionId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TransactionStatus Status { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class ValidationProblemResponse
{
    public Dictionary<string, string[]> Errors { get; set; } = [];
}

public enum TransactionStatus
{
    Pending = 0,
    Validated = 1,
    Processing = 2,
    Processed = 3,
    Failed = 4
}

public enum AccountActivityDirection
{
    Outgoing = 0,
    Incoming = 1
}

public sealed class CurrencyStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("code", out var codeElement))
            {
                return codeElement.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("Code", out var codePascalElement))
            {
                return codePascalElement.GetString() ?? string.Empty;
            }
        }

        throw new JsonException("Could not deserialize currency value.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
