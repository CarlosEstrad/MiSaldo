namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when an amount that must be strictly positive (an income value, an
/// expense budget, a payment) is zero or negative.
/// </summary>
public sealed class NonPositiveAmountException : DomainException
{
    public NonPositiveAmountException(string what, decimal amount)
        : base($"{what} must be greater than zero, but was {amount}.")
    {
        What = what;
        Amount = amount;
    }

    public string What { get; }

    public decimal Amount { get; }
}
