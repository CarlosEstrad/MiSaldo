namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when a financial period is created with an end date earlier than its
/// start date (RNF-DAT-05).
/// </summary>
public sealed class InvalidPeriodRangeException : DomainException
{
    public InvalidPeriodRangeException(DateOnly startDate, DateOnly endDate)
        : base($"Period end date {endDate:yyyy-MM-dd} cannot be earlier than start date {startDate:yyyy-MM-dd}.")
    {
        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }
}
