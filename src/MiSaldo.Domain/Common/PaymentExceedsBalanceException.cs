namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when a credit payment is larger than the outstanding balance. A
/// payment can never exceed what is still owed (RN-C05, RF-CRD-11).
/// </summary>
public sealed class PaymentExceedsBalanceException : DomainException
{
    public PaymentExceedsBalanceException(Money payment, Money outstandingBalance)
        : base($"Payment {payment} exceeds the outstanding balance {outstandingBalance}.")
    {
        Payment = payment;
        OutstandingBalance = outstandingBalance;
    }

    public Money Payment { get; }

    public Money OutstandingBalance { get; }
}
