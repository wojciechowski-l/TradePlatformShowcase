using TradePlatform.Core.Constants;

namespace TradePlatform.Core.DTOs;

public record TransactionStatusChangedEvent(
    Guid TransactionId,
    string SourceAccountId,
    string TargetAccountId,
    decimal Amount,
    string Currency,
    TransactionStatus PreviousStatus,
    TransactionStatus CurrentStatus,
    DateTime ChangedAtUtc,
    string? FailureReason = null
);
