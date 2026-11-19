namespace MiSaldo.Application.Common;

/// <summary>
/// Raised by the unit of work when a save breaks a uniqueness rule in the
/// database, for example two registrations racing with the same email. Use
/// cases turn it into a <see cref="ErrorType.Conflict"/> result.
/// </summary>
public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(string? constraintName, Exception innerException)
        : base($"A uniqueness rule was broken ({constraintName ?? "unknown constraint"}).", innerException)
    {
        ConstraintName = constraintName;
    }

    public string? ConstraintName { get; }
}

/// <summary>
/// Raised by the unit of work when another request changed the same row first
/// (optimistic concurrency, RNF-DAT-04), for example two requests consuming the
/// same token at once.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The data was changed by another request.", innerException)
    {
    }
}
