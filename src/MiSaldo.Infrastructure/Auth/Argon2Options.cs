namespace MiSaldo.Infrastructure.Auth;

/// <summary>
/// Cost parameters for Argon2id. The defaults (64 MiB, 3 passes, 4 lanes) follow
/// the OWASP guidance. They are stored inside each hash, so they can be raised
/// later without invalidating hashes that already exist.
/// </summary>
public sealed record Argon2Options
{
    public int MemorySizeKiB { get; init; } = 65_536;

    public int Iterations { get; init; } = 3;

    public int Parallelism { get; init; } = 4;

    public void Validate()
    {
        if (Parallelism is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(Parallelism), Parallelism, "Parallelism must be between 1 and 64.");
        }

        if (Iterations is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations), Iterations, "Iterations must be between 1 and 100.");
        }

        // Argon2 needs at least 8 KiB of memory per lane.
        if (MemorySizeKiB < 8 * Parallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MemorySizeKiB), MemorySizeKiB, "Memory must be at least 8 KiB per lane.");
        }
    }
}
