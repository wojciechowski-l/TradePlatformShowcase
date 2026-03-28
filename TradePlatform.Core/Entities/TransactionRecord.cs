using TradePlatform.Core.Constants;
using TradePlatform.Core.ValueObjects;

namespace TradePlatform.Core.Entities
{
    public class TransactionRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string SourceAccountId { get; set; } = string.Empty;
        public virtual Account? SourceAccount { get; set; }
        public string TargetAccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public required Currency Currency { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
        public DateTime? ValidatedAtUtc { get; set; }
        public DateTime? ProcessingStartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? FailureReason { get; set; }

        public TransactionStatus MarkValidated(DateTime validatedAtUtc)
        {
            EnsureTransition(TransactionStatus.Pending, TransactionStatus.Validated);

            var previousStatus = Status;
            Status = TransactionStatus.Validated;
            ValidatedAtUtc = validatedAtUtc;
            FailureReason = null;

            return previousStatus;
        }

        public TransactionStatus MarkProcessing(DateTime processingStartedAtUtc)
        {
            EnsureTransition(TransactionStatus.Validated, TransactionStatus.Processing);

            var previousStatus = Status;
            Status = TransactionStatus.Processing;
            ProcessingStartedAtUtc = processingStartedAtUtc;

            return previousStatus;
        }

        public TransactionStatus MarkProcessed(DateTime completedAtUtc)
        {
            EnsureTransition(TransactionStatus.Processing, TransactionStatus.Processed);

            var previousStatus = Status;
            Status = TransactionStatus.Processed;
            CompletedAtUtc = completedAtUtc;
            FailureReason = null;

            return previousStatus;
        }

        public TransactionStatus MarkFailed(DateTime completedAtUtc, string failureReason)
        {
            if (Status is TransactionStatus.Processed or TransactionStatus.Failed)
            {
                throw new InvalidOperationException($"Transaction cannot transition from {Status} to {TransactionStatus.Failed}.");
            }

            var previousStatus = Status;
            Status = TransactionStatus.Failed;
            CompletedAtUtc = completedAtUtc;
            FailureReason = failureReason;

            return previousStatus;
        }

        private void EnsureTransition(TransactionStatus expectedCurrentStatus, TransactionStatus targetStatus)
        {
            if (Status != expectedCurrentStatus)
            {
                throw new InvalidOperationException($"Transaction cannot transition from {Status} to {targetStatus}. Expected {expectedCurrentStatus}.");
            }
        }
    }
}
