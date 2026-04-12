namespace TradePlatform.Infrastructure.Configuration;

public class MessagingReliabilityOptions
{
    public const string SectionName = "MessagingReliability";

    public TimeSpan RetentionSweepInterval { get; set; } = TimeSpan.FromHours(1);
    public int RetentionBatchSize { get; set; } = 500;
    public int MaxBatchesPerSweep { get; set; } = 10;
    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan IdempotencyRetention { get; set; } = TimeSpan.FromDays(2);
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromSeconds(15);
}
