namespace TradePlatform.Core.ValueObjects
{
    public record Currency
    {
        public string Code { get; }

        private Currency(string code)
        {
            Code = code;
        }

        public static Currency FromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            {
                throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(code));
            }
            return new Currency(code.ToUpperInvariant());
        }

        public override string ToString() => Code;
    }
}