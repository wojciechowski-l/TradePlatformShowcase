namespace TradePlatform.Core.DTOs
{
    public record TransactionCreatedEvent(
        Guid TransactionId,
        string SourceAccountId,
        string TargetAccountId,
        decimal Amount,
        string Currency
    );
}
