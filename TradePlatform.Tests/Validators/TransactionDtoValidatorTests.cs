using FluentValidation.TestHelper;
using TradePlatform.Api.Validators;
using TradePlatform.Core.DTOs;

namespace TradePlatform.Tests.Validators
{
    public class TransactionDtoValidatorTests
    {
        private readonly TransactionDtoValidator _validator = new();

        [Fact]
        public void Should_Have_Error_When_Source_Equals_Target()
        {
            var model = new TransactionDto
            {
                SourceAccountId = "ACC1",
                TargetAccountId = "ACC1",
                Amount = 100,
                Currency = "USD"
            };

            var result = _validator.TestValidate(model);

            result.ShouldHaveValidationErrorFor(x => x.TargetAccountId)
                  .WithErrorMessage("Source and Target accounts must be different.");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-10)]
        public void Should_Have_Error_When_Amount_Is_Invalid(decimal amount)
        {
            var model = new TransactionDto { Amount = amount };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Amount);
        }

        [Fact]
        public void Should_Have_Error_When_Currency_Is_Invalid()
        {
            var model = new TransactionDto { Currency = "US" };
            var result = _validator.TestValidate(model);
            result.ShouldHaveValidationErrorFor(x => x.Currency);
        }
    }
}