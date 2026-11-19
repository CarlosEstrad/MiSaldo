namespace MiSaldo.Infrastructure.Email;

/// <summary>How the connection to the SMTP server is protected.</summary>
public enum SmtpSecurity
{
    /// <summary>Plain connection upgraded with STARTTLS (port 587: Gmail, SES and most providers).</summary>
    StartTls,

    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect,

    /// <summary>No encryption. Only for a local server such as Mailpit; refused in Production.</summary>
    None,
}

/// <summary>
/// SMTP settings for the account emails (D-13), bound from the <c>Smtp</c>
/// section (<c>Smtp__Host</c>, <c>Smtp__Port</c>, ...). The same settings
/// serve Mailpit, a dedicated Gmail account or a transactional provider: only
/// the values change. The password comes from the environment or a secret
/// store, never from the repository (RNF-SEG-08).
/// </summary>
public sealed record SmtpOptions
{
    public const string SectionName = "Smtp";
    public const int MaxTimeoutSeconds = 120;

    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    public SmtpSecurity Security { get; init; } = SmtpSecurity.StartTls;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? FromAddress { get; init; }

    public string FromName { get; init; } = "MiSaldo";

    /// <summary>Longest wait for the server, so a slow one does not hang the request (R3.3).</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>True when a server is set; otherwise the in-memory notifier is used outside Production.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// The problems with these settings, as messages that name the setting but
    /// never show its value, so a password cannot end up in a startup error.
    /// </summary>
    public IReadOnlyList<string> Validate(bool isProduction)
    {
        var problems = new List<string>();
        if (!IsConfigured)
        {
            if (isProduction)
            {
                problems.Add($"{SectionName}:Host is required in Production: account emails cannot be delivered without it.");
            }

            return problems;
        }

        if (Port is < 1 or > 65535)
        {
            problems.Add($"{SectionName}:Port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(Security))
        {
            problems.Add($"{SectionName}:Security must be StartTls, SslOnConnect or None.");
        }
        else if (Security == SmtpSecurity.None && isProduction)
        {
            problems.Add($"{SectionName}:Security cannot be None in Production: credentials and links would travel unencrypted.");
        }

        if (!IsPlausibleAddress(FromAddress))
        {
            problems.Add($"{SectionName}:FromAddress must be a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(FromName))
        {
            problems.Add($"{SectionName}:FromName cannot be empty.");
        }

        if (string.IsNullOrEmpty(Username) != string.IsNullOrEmpty(Password))
        {
            problems.Add($"{SectionName}:Username and {SectionName}:Password must be set together, or both left empty.");
        }

        if (TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            problems.Add($"{SectionName}:TimeoutSeconds must be between 1 and {MaxTimeoutSeconds}.");
        }

        return problems;
    }

    // The sender is set by the operator, not a visitor: a simple shape check is enough.
    private static bool IsPlausibleAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = address.IndexOf('@');
        return at > 0 && at == address.LastIndexOf('@') && address.IndexOf('.', at) > at + 1 && !address.EndsWith('.');
    }
}
