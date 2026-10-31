namespace MiSaldo.Domain.Accounts;

/// <summary>
/// The kind of account. Only <see cref="Standard"/> exists at launch; the model
/// anticipates <c>Child</c> accounts for a future family feature (sección 7).
/// </summary>
public enum AccountType
{
    Standard,
    Child,
}
