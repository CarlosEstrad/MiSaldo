namespace MiSaldo.Domain.Expenses;

/// <summary>
/// The derived state of an expense based on how much of its budget is paid
/// (RF-GAS-03). It is never stored as editable data; it is computed from the
/// payments.
/// </summary>
public enum ExpenseStatus
{
    /// <summary>Nothing paid yet (paid = 0).</summary>
    Pending,

    /// <summary>Some but not all of the budget is paid (0 &lt; paid &lt; budgeted).</summary>
    Partial,

    /// <summary>The budget is fully covered (paid &gt;= budgeted).</summary>
    Paid,
}
