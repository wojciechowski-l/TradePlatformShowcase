namespace TradePlatform.Core.DTOs
{
    public record TransactionSubmittedEvent(
        Guid TransactionId,
        string SourceAccountId,
        string TargetAccountId,
        decimal Amount,
        string Currency,
        DateTime SubmittedAtUtc
    );
}
