using FluentValidation;
using TradePlatform.Core.DTOs;

namespace TradePlatform.Api.Validators
{
    public class TransactionDtoValidator : AbstractValidator<TransactionDto>
    {
        public TransactionDtoValidator()
        {
            RuleFor(x => x.SourceAccountId)
                .NotEmpty().WithMessage("Source Account ID is required.");

            RuleFor(x => x.TargetAccountId)
                .NotEmpty().WithMessage("Target Account ID is required.")
                .NotEqual(x => x.SourceAccountId)
                .WithMessage("Source and Target accounts must be different.");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("Amount must be greater than zero.")
                .LessThan(1_000_000_000_000_000).WithMessage("Amount is too large.");

            RuleFor(x => x.Currency)
                .NotEmpty()
                .Length(3).WithMessage("Currency must be a 3-letter ISO code.")
                .Matches("^[A-Z]{3}$").WithMessage("Currency must be uppercase letters.");
        }
    }
}