namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when a savings withdrawal is larger than the available balance of the
/// goal or the general savings fund (RF-AHO-04).
/// </summary>
public sealed class WithdrawalExceedsSavingsException : DomainException
{
    public WithdrawalExceedsSavingsException(Money withdrawal, Money availableBalance)
        : base($"Withdrawal {withdrawal} exceeds the available savings balance {availableBalance}.")
    {
        Withdrawal = withdrawal;
        AvailableBalance = availableBalance;
    }

    public Money Withdrawal { get; }

    public Money AvailableBalance { get; }
}
