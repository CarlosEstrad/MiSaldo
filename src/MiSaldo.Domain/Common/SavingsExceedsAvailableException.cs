namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when a savings contribution is larger than the money available in the
/// period. A contribution must be less than or equal to the available amount
/// (D-10, RF-AHO-08).
/// </summary>
public sealed class SavingsExceedsAvailableException : DomainException
{
    public SavingsExceedsAvailableException(Money contribution, Money availableMoney)
        : base($"Savings contribution {contribution} exceeds the money available in the period {availableMoney}.")
    {
        Contribution = contribution;
        AvailableMoney = availableMoney;
    }

    public Money Contribution { get; }

    public Money AvailableMoney { get; }
}
