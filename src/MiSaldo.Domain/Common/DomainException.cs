namespace MiSaldo.Domain.Common;

/// <summary>
/// Base type for all business-rule violations in the domain. Technical errors
/// (argument validation, infrastructure) do not derive from this type, so the
/// application layer can distinguish a broken invariant from a bug and map it
/// to a Problem Details response. Messages are neutral and contain no UI text.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }
}
