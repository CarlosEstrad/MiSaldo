using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Expenses;

/// <summary>
/// A budgeted expense with zero or more payments. The budgeted amount is what
/// the user expects to spend; the status and outstanding obligation are derived
/// from the payments, never stored as editable data (RF-GAS-01 to RF-GAS-05,
/// RN-F04, RN-M06).
/// </summary>
public sealed class Expense
{
    private readonly List<ExpensePayment> _payments = [];

    // For ORM materialization only: EF Core sets the mapped properties, including
    // the Money complex values, after construction. Application code uses the
    // public constructor, which enforces the invariants.
    private Expense()
    {
        Name = string.Empty;
    }

    public Expense(
        Guid id,
        string name,
        Guid categoryId,
        Money budgeted,
        DateOnly date,
        ExpenseType type)
    {
        if (!budgeted.IsPositive)
        {
            throw new NonPositiveAmountException("Expense budget", budgeted.Amount);
        }

        Id = id;
        Name = name;
        CategoryId = categoryId;
        Budgeted = budgeted;
        Date = date;
        Type = type;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public Guid CategoryId { get; private set; }

    public Money Budgeted { get; private set; }

    /// <summary>The expense date. The outstanding obligation belongs to this period (RN-M02).</summary>
    public DateOnly Date { get; private set; }

    public ExpenseType Type { get; private set; }

    public IReadOnlyList<ExpensePayment> Payments => _payments;

    public Money TotalPaid()
    {
        var total = Money.Zero(Budgeted.Currency);
        foreach (var payment in _payments)
        {
            total += payment.Value;
        }

        return total;
    }

    /// <summary>
    /// The remaining obligation: <c>max(0, budgeted - total paid)</c>. Overpaying
    /// leaves it at zero (RN-F04, RN-M06).
    /// </summary>
    public Money Outstanding()
    {
        var remaining = Budgeted - TotalPaid();
        return remaining.IsPositive ? remaining : Money.Zero(Budgeted.Currency);
    }

    /// <summary>The derived status based on the total paid (RF-GAS-03).</summary>
    public ExpenseStatus Status()
    {
        var paid = TotalPaid();
        if (paid.IsZero)
        {
            return ExpenseStatus.Pending;
        }

        return paid >= Budgeted ? ExpenseStatus.Paid : ExpenseStatus.Partial;
    }

    /// <summary>
    /// Edits the expense (RF-GAS-06). Payments are not touched: they are an
    /// immutable history (RN-M08), so the budget keeps their currency.
    /// </summary>
    public void Update(string name, Guid categoryId, Money budgeted, DateOnly date, ExpenseType type)
    {
        if (!budgeted.IsPositive)
        {
            throw new NonPositiveAmountException("Expense budget", budgeted.Amount);
        }

        if (!budgeted.Currency.Equals(Budgeted.Currency))
        {
            throw new CurrencyMismatchException(Budgeted.Currency, budgeted.Currency);
        }

        Name = name;
        CategoryId = categoryId;
        Budgeted = budgeted;
        Date = date;
        Type = type;
    }

    /// <summary>
    /// Records a payment. Overpayment is allowed (RN-M06). The payment must be in
    /// the same currency as the budget.
    /// </summary>
    public void AddPayment(ExpensePayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!payment.Value.Currency.Equals(Budgeted.Currency))
        {
            throw new CurrencyMismatchException(Budgeted.Currency, payment.Value.Currency);
        }

        _payments.Add(payment);
    }
}
