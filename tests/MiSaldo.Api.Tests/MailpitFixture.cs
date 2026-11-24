using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MiSaldo.Infrastructure.Email;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// A real Mailpit in a container: an SMTP server that keeps every email and
/// exposes it through an HTTP API, so tests can read what the system sent.
/// SMTP authentication accepts any credentials (or none) over plain SMTP, so
/// the authenticated path can be tested too. Needs Docker.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public const string Image = "axllent/mailpit:v1.34";
    public const string FromAddress = "no-reply@misaldo.test";
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "1")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "1")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/api/v1/info")))
        .Build();

    private HttpClient? _http;

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(SmtpPort);

    public MailpitClient Client => new(_http ?? throw new InvalidOperationException("Mailpit is not started."));

    /// <summary>Settings for the notifier: plain SMTP, as in local development.</summary>
    public SmtpOptions Options(string? username = null, string? password = null, int timeoutSeconds = 10) => new()
    {
        Host = Host,
        Port = Port,
        Security = SmtpSecurity.None,
        Username = username,
        Password = password,
        FromAddress = FromAddress,
        FromName = "MiSaldo",
        TimeoutSeconds = timeoutSeconds,
    };

    /// <summary>The same settings as configuration keys, to host the API against this Mailpit.</summary>
    public Dictionary<string, string?> Settings(string frontendBaseUrl = "http://localhost:4200") => new()
    {
        ["Smtp:Host"] = Host,
        ["Smtp:Port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Smtp:Security"] = nameof(SmtpSecurity.None),
        ["Smtp:FromAddress"] = FromAddress,
        ["Frontend:BaseUrl"] = frontendBaseUrl,
    };

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        await _container.DisposeAsync();
    }
}

/// <summary>A small client for the parts of the Mailpit HTTP API the tests use.</summary>
public sealed class MailpitClient(HttpClient http)
{
    /// <summary>Waits until an email to <paramref name="address"/> arrives and returns it in full.</summary>
    public async Task<MailpitMessage> WaitForMessageToAsync(string address, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var found = await SearchToAsync(address, cancellationToken);
            if (found.Count > 0)
            {
                return await GetAsync(found[0].Id, cancellationToken);
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No email to {address} arrived in Mailpit.");
            }

            await Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>Waits until an email to <paramref name="address"/> with this exact subject arrives and returns it in full.</summary>
    public async Task<MailpitMessage> WaitForMessageAsync(
        string address, string subject, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var found = (await SearchToAsync(address, cancellationToken))
                .FirstOrDefault(m => string.Equals(m.Subject, subject, StringComparison.Ordinal));
            if (found is not null)
            {
                return await GetAsync(found.Id, cancellationToken);
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No email \"{subject}\" to {address} arrived in Mailpit.");
            }

            await Task.Delay(200, cancellationToken);
        }
    }

    /// <summary>The emails sent to <paramref name="address"/>, newest first.</summary>
    public async Task<IReadOnlyList<MailpitSummary>> SearchToAsync(string address, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString($"to:\"{address}\"");
        var page = await http.GetFromJsonAsync<MailpitPage>($"/api/v1/search?query={query}", cancellationToken);
        return page?.Messages ?? [];
    }

    public async Task<MailpitMessage> GetAsync(string id, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<MailpitMessage>($"/api/v1/message/{Uri.EscapeDataString(id)}", cancellationToken)
        ?? throw new InvalidOperationException($"Mailpit has no message {id}.");
}

public sealed record MailpitAddress(
    [property: JsonPropertyName("Name")] string Name,
    [property: JsonPropertyName("Address")] string Address);

public sealed record MailpitSummary(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("Subject")] string Subject);

public sealed record MailpitPage([property: JsonPropertyName("messages")] IReadOnlyList<MailpitSummary> Messages);

public sealed record MailpitMessage(
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("From")] MailpitAddress From,
    [property: JsonPropertyName("To")] IReadOnlyList<MailpitAddress> To,
    [property: JsonPropertyName("Subject")] string Subject,
    [property: JsonPropertyName("Text")] string Text,
    [property: JsonPropertyName("HTML")] string Html);
