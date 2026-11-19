namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords with a strong, salted algorithm. Passwords are
/// stored only as hashes and never logged (RNF-SEG-10).
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
