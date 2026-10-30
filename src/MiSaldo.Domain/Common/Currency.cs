namespace MiSaldo.Domain.Common;

/// <summary>
/// An ISO currency with the number of decimal places used for rounding and
/// display. The domain never hardcodes decimal places; it reads them here
/// (RNF-DAT-02). COP uses 0 decimals, USD uses 2.
/// </summary>
public sealed record Currency
{
    /// <summary>Colombian peso: 0 decimal places.</summary>
    public static readonly Currency Cop = new("COP", 0);

    /// <summary>US dollar: 2 decimal places.</summary>
    public static readonly Currency Usd = new("USD", 2);

    public Currency(string code, int decimalPlaces)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Currency code is required.", nameof(code));
        }

        if (code.Trim().Length != 3)
        {
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO code.", nameof(code));
        }

        if (decimalPlaces is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalPlaces),
                decimalPlaces,
                "Decimal places must be between 0 and 4.");
        }

        Code = code.Trim().ToUpperInvariant();
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>The 3-letter ISO currency code, uppercased.</summary>
    public string Code { get; }

    /// <summary>Number of decimal places this currency rounds and displays to.</summary>
    public int DecimalPlaces { get; }

    public override string ToString() => Code;
}
