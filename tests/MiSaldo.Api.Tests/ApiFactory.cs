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

    private readonly string? _signingKey;
    private readonly string? _connectionString;

    public ApiFactory()
        : this(TestSigningKey, "Host=localhost;Port=1;Database=unused;Username=x;Password=x")
    {
    }

    private ApiFactory(string? signingKey, string? connectionString)
    {
        _signingKey = signingKey;
        _connectionString = connectionString;
    }

    /// <summary>A host with custom settings, to check what happens when one is missing.</summary>
    public static ApiFactory With(string? signingKey, string? connectionString) => new(signingKey, connectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:SigningKey", _signingKey ?? string.Empty);
        builder.UseSetting("JWT:SIGNING_KEY", string.Empty);
        builder.UseSetting("ConnectionStrings:MiSaldo", _connectionString ?? string.Empty);
        builder.UseSetting("MISALDO_DB", string.Empty);
    }
}
