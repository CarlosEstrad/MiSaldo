namespace MiSaldo.Domain.Incomes;

/// <summary>
/// Whether an income is still expected or has actually been received. Only a
/// received income affects the period's cash (RF-ING-02, RN-F01, RN-F02).
/// </summary>
public enum IncomeStatus
{
    /// <summary>Expected income that has not been received. Does not affect cash.</summary>
    Planned,

    /// <summary>Income actually received. Affects the period's cash.</summary>
    Received,
}
