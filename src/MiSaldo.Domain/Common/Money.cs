namespace MiSaldo.Domain.Common;

/// <summary>
/// An immutable monetary amount in a single currency. Arithmetic is exact
/// (decimal) and does not round intermediate results; rounding happens only
/// when a total is exposed via <see cref="Round"/>, so no money is lost or
/// created (RN-M07). Amounts in different currencies cannot be combined.
/// </summary>
public readonly record struct Money
{
    public Money(decimal amount, Currency currency)
    {
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
        Amount = amount;
    }

    /// <summary>The exact amount. Not rounded until <see cref="Round"/> is called.</summary>
    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;

    public bool IsPositive => Amount > 0m;

    public bool IsNegative => Amount < 0m;

    /// <summary>
    /// Rounds the amount to the currency's decimal places using banker's
    /// rounding (half to even), which has no bias over many operations (RN-M07).
    /// </summary>
    public Money Round() =>
        new(Math.Round(Amount, Currency.DecimalPlaces, MidpointRounding.ToEven), Currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static bool operator >(Money left, Money right)
    {
        left.EnsureSameCurrency(right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        left.EnsureSameCurrency(right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        left.EnsureSameCurrency(right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        left.EnsureSameCurrency(right);
        return left.Amount <= right.Amount;
    }

    public override string ToString() =>
        Amount.ToString("F" + Currency.DecimalPlaces, System.Globalization.CultureInfo.InvariantCulture)
        + " " + Currency.Code;
}
