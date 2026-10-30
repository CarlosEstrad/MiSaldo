namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when an operation combines amounts in different currencies. MiSaldo
/// assumes a single primary currency per user, so cross-currency arithmetic is
/// a broken invariant rather than a supported feature.
/// </summary>
public sealed class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot operate on amounts in different currencies: {left.Code} and {right.Code}.")
    {
        Left = left;
        Right = right;
    }

    public Currency Left { get; }

    public Currency Right { get; }
}
