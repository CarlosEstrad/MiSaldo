using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Credits;

/// <summary>
/// An interest-free credit paid in equal installments. The financed amount
/// (<c>OriginalValue - DownPayment</c>) is split equally across the installments,
/// with the last one absorbing any rounding residue so the schedule always sums
/// to exactly the financed amount (RF-CRD-02, RN-C03). Payments are immutable;
/// a payment that differs from the scheduled installment recalculates only the
/// future open installments (RF-CRD-05 to RF-CRD-12, RN-C01 to RN-C06, D-06).
/// </summary>
public sealed class Credit
{
    private readonly List<CreditInstallment> _installments = [];
    private readonly List<CreditPayment> _payments = [];
    private readonly Currency _currency;
    private readonly PaymentFrequency _frequency;
    private DateOnly _lastDueDate;

    public Credit(
        Guid id,
        string name,
        Money originalValue,
        Money downPayment,
        int installmentCount,
        DateOnly firstDueDate,
        PaymentFrequency frequency)
    {
        ArgumentNullException.ThrowIfNull(originalValue.Currency);

        if (!originalValue.IsPositive)
        {
            throw new NonPositiveAmountException("Credit original value", originalValue.Amount);
        }

        if (downPayment.IsNegative)
        {
            throw new NonPositiveAmountException("Credit down payment", downPayment.Amount);
        }

        if (!downPayment.Currency.Equals(originalValue.Currency))
        {
            throw new CurrencyMismatchException(originalValue.Currency, downPayment.Currency);
        }

        if (downPayment > originalValue)
        {
            throw new ArgumentException(
                "Down payment cannot exceed the original value.", nameof(downPayment));
        }

        if (installmentCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(installmentCount), installmentCount, "A credit needs at least one installment.");
        }

        Id = id;
        Name = name;
        OriginalValue = originalValue;
        DownPayment = downPayment;
        _currency = originalValue.Currency;
        _frequency = frequency;
        Status = CreditStatus.Active;

        GenerateSchedule(installmentCount, firstDueDate);
    }

    public Guid Id { get; }

    public string Name { get; }

    public Money OriginalValue { get; }

    public Money DownPayment { get; }

    public CreditStatus Status { get; private set; }

    public IReadOnlyList<CreditInstallment> Installments => _installments;

    public IReadOnlyList<CreditPayment> Payments => _payments;

    /// <summary>The amount financed: original value minus down payment.</summary>
    public Money Financed => OriginalValue - DownPayment;

    /// <summary>
    /// The outstanding balance: <c>OriginalValue - DownPayment - sum of payments</c>
    /// (RN-C01). Never negative.
    /// </summary>
    public Money OutstandingBalance()
    {
        var balance = Financed;
        foreach (var payment in _payments)
        {
            balance -= payment.Value;
        }

        return balance.IsNegative ? Money.Zero(_currency) : balance;
    }

    /// <summary>
    /// Registers a payment and recalculates the future open installments
    /// (RF-CRD-05, RF-CRD-06). The payment must be positive and must not exceed
    /// the outstanding balance (RN-C05). Existing payments are never modified
    /// (RN-C06).
    /// </summary>
    public void RegisterPayment(CreditPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!payment.Value.Currency.Equals(_currency))
        {
            throw new CurrencyMismatchException(_currency, payment.Value.Currency);
        }

        var balanceBefore = OutstandingBalance();
        if (payment.Value > balanceBefore)
        {
            throw new PaymentExceedsBalanceException(payment.Value, balanceBefore);
        }

        _payments.Add(payment);

        ApplyToFirstOpenInstallment(payment.Value);

        var remaining = OutstandingBalance();
        if (remaining.IsZero)
        {
            CloseFullyPaid();
            return;
        }

        RedistributeOpenInstallments(remaining);
    }

    // --- schedule generation (RF-CRD-02, RN-C03) ---

    private void GenerateSchedule(int count, DateOnly firstDueDate)
    {
        var amounts = SplitEqually(Financed, count);
        var due = firstDueDate;
        for (int i = 0; i < count; i++)
        {
            _installments.Add(new CreditInstallment(i + 1, amounts[i], due));
            _lastDueDate = due;
            due = NextDueDate(due);
        }
    }

    // Splits an amount into 'count' equal parts rounded to the currency's
    // decimals; the last part absorbs the residue so the sum is exact.
    private List<Money> SplitEqually(Money total, int count)
    {
        var result = new List<Money>(count);
        var baseShare = new Money(total.Amount / count, _currency).Round();
        var accumulated = Money.Zero(_currency);

        for (int i = 0; i < count - 1; i++)
        {
            result.Add(baseShare);
            accumulated += baseShare;
        }

        result.Add(total - accumulated); // last absorbs the residue
        return result;
    }

    // --- payment application and recalculation (RN-C02, RN-C03, RN-C04) ---

    // A payment settles the first open installment slot regardless of whether it
    // fully covered the scheduled amount: a smaller payment pushes the shortfall
    // into the remaining installments via redistribution, a larger one reduces
    // them (RN-C02, CP-05, CP-06). The payment value itself is not needed here;
    // the outstanding balance already reflects it.
    private void ApplyToFirstOpenInstallment(Money paymentValue)
    {
        _ = paymentValue;
        var firstOpen = _installments.FirstOrDefault(i => i.IsOpen);
        firstOpen?.MarkPaid();
    }

    private void RedistributeOpenInstallments(Money remaining)
    {
        var open = _installments.Where(i => i.IsOpen).ToList();
        if (open.Count == 0)
        {
            // Balance remains but no open installment to carry it: add one a
            // period after the last due date with the shortfall (RN-C04).
            AddCatchUpInstallment(remaining);
            return;
        }

        var shares = SplitEqually(remaining, open.Count);
        for (int i = 0; i < open.Count; i++)
        {
            open[i].SetAmount(shares[i]);
        }
    }

    private void AddCatchUpInstallment(Money shortfall)
    {
        var due = NextDueDate(_lastDueDate);
        var number = _installments.Count + 1;
        _installments.Add(new CreditInstallment(number, shortfall, due));
        _lastDueDate = due;
    }

    private void CloseFullyPaid()
    {
        foreach (var installment in _installments.Where(i => i.IsOpen))
        {
            installment.Cancel();
        }

        Status = CreditStatus.Paid;
    }

    private DateOnly NextDueDate(DateOnly from) => _frequency switch
    {
        PaymentFrequency.Monthly => from.AddMonths(1),
        PaymentFrequency.Biweekly => from.AddDays(14),
        PaymentFrequency.Weekly => from.AddDays(7),
        _ => throw new ArgumentOutOfRangeException(nameof(from)),
    };
}
