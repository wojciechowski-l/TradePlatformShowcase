using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs;

public record TransactionProcessedEvent(
    Guid TransactionId,
    string AccountId,
    TransactionStatus Status,
    DateTime ProcessedAtUtc
);