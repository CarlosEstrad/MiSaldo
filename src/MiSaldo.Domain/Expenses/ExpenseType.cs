namespace MiSaldo.Domain.Expenses;

/// <summary>
/// The nature of an expense (RF-GAS-01).
/// </summary>
public enum ExpenseType
{
    Fixed,
    Variable,
    Extraordinary,
    Recurring,
}
