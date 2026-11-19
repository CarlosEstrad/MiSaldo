using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using MiSaldo.Application.Abstractions;

namespace MiSaldo.Infrastructure.Auth;

/// <summary>
/// Hashes passwords with Argon2id and a random salt per password. The result is
/// a self-describing string, <c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>, so
/// verification uses the parameters stored in the hash itself (RNF-SEG-10).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltLength = 16;
    private const int HashLength = 32;
    private const string Prefix = "$argon2id$v=19$";

    // Sanity bounds applied when reading a stored hash, so a corrupted value can
    // never make us allocate absurd amounts of memory.
    private const int MaxMemoryKiB = 1_048_576;
    private const int MaxIterations = 100;
    private const int MaxParallelism = 64;

    private readonly Argon2Options _options;

    public PasswordHasher(Argon2Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Compute(
            password, salt, _options.MemorySizeKiB, _options.Iterations, _options.Parallelism, HashLength);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}m={_options.MemorySizeKiB},t={_options.Iterations},p={_options.Parallelism}${ToBase64(salt)}${ToBase64(hash)}");
    }

    public bool Verify(string passwordHash, string password)
    {
        // Hash() never accepts an empty password, so an empty one can never
        // match. Rejecting it here also keeps the Argon2 library from throwing,
        // which would turn a bad login into a server error.
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        if (!TryParse(passwordHash, out var parsed))
        {
            return false;
        }

        var actual = Compute(
            password, parsed.Salt, parsed.MemoryKiB, parsed.Iterations, parsed.Parallelism, parsed.Hash.Length);

        return CryptographicOperations.FixedTimeEquals(actual, parsed.Hash);
    }

    private static byte[] Compute(
        string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int hashLength)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        return argon2.GetBytes(hashLength);
    }

    private static bool TryParse(string encoded, out ParsedHash parsed)
    {
        parsed = default;

        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // After the prefix: "m=..,t=..,p=..$salt$hash"
        var parts = encoded[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        var parameters = parts[0].Split(',');
        if (parameters.Length != 3
            || !TryReadInt(parameters[0], "m=", out var memory)
            || !TryReadInt(parameters[1], "t=", out var iterations)
            || !TryReadInt(parameters[2], "p=", out var parallelism))
        {
            return false;
        }

        if (parallelism is < 1 or > MaxParallelism
            || iterations is < 1 or > MaxIterations
            || memory < 8 * parallelism
            || memory > MaxMemoryKiB)
        {
            return false;
        }

        if (!TryFromBase64(parts[1], out var salt)
            || !TryFromBase64(parts[2], out var hash)
            || salt.Length < 8
            || hash.Length is < 16 or > 128)
        {
            return false;
        }

        parsed = new ParsedHash(memory, iterations, parallelism, salt, hash);
        return true;
    }

    private static bool TryReadInt(string text, string prefix, out int value)
    {
        value = 0;
        return text.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(text[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // Base64 without padding, as in the standard Argon2 string format.
    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromBase64(string text, out byte[] bytes)
    {
        var padded = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private readonly record struct ParsedHash(
        int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
