using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs;

public record TransactionProcessedEvent(
    Guid TransactionId,
    string SourceAccountId,
    string TargetAccountId,
    decimal Amount,
    string Currency,
    TransactionStatus Status,
    DateTime ProcessedAtUtc
);
