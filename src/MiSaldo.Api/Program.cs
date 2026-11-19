using MiSaldo.Api;
using MiSaldo.Api.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);
}
else
{
    // Structured logs, one JSON object per line, with the correlation id scope (RNF-MAN-05).
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
    });
}

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddMiSaldoApi(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

/// <summary>Entry point; public so the integration tests can host the API.</summary>
public partial class Program;
