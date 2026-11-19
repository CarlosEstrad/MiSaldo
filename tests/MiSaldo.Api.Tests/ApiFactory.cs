using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Hosts the real API in memory. The connection string points nowhere: these
/// smoke tests never touch the database, so they run without Docker.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestSigningKey = "test-signing-key-that-is-long-enough-for-hs256";
    public const string UnusedConnectionString = "Host=localhost;Port=1;Database=unused;Username=x;Password=x";

    private readonly string? _signingKey;
    private readonly string? _connectionString;
    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public ApiFactory()
        : this(TestSigningKey, UnusedConnectionString, "Development", new Dictionary<string, string?>())
    {
    }

    private ApiFactory(
        string? signingKey, string? connectionString, string environment, IReadOnlyDictionary<string, string?> settings)
    {
        _signingKey = signingKey;
        _connectionString = connectionString;
        _environment = environment;
        _settings = settings;
    }

    /// <summary>A host with custom settings, to check what happens when one is missing.</summary>
    public static ApiFactory With(string? signingKey, string? connectionString) =>
        new(signingKey, connectionString, "Development", new Dictionary<string, string?>());

    /// <summary>A host in another environment (for example Production) with extra settings.</summary>
    public static ApiFactory In(string environment, IReadOnlyDictionary<string, string?>? settings = null) =>
        new(TestSigningKey, UnusedConnectionString, environment, settings ?? new Dictionary<string, string?>());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Jwt:SigningKey", _signingKey ?? string.Empty);
        builder.UseSetting("JWT:SIGNING_KEY", string.Empty);
        builder.UseSetting("ConnectionStrings:MiSaldo", _connectionString ?? string.Empty);
        builder.UseSetting("MISALDO_DB", string.Empty);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
