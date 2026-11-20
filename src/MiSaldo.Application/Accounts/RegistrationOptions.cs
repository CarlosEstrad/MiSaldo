using MiSaldo.Domain.Common;

namespace MiSaldo.Application.Accounts;

/// <summary>
/// Settings applied when an account is created: the starting preferences and
/// the versions of the legal documents the user accepts (RF-PRV-01).
/// </summary>
public sealed record RegistrationOptions
{
    /// <summary>
    /// Colombia (UTC-5, no daylight saving time). Built as a fixed zone so it
    /// does not depend on the time zone database of the host.
    /// </summary>
    public static TimeZoneInfo Colombia { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "America/Bogota", TimeSpan.FromHours(-5), "Colombia", "Colombia Standard Time");

    /// <summary>The zone used to decide "today" for the age check, before the user has settings.</summary>
    public TimeZoneInfo TimeZone { get; init; } = Colombia;

    public string PrivacyPolicyVersion { get; init; } = "1.0";

    public string TermsVersion { get; init; } = "1.0";

    public Currency DefaultCurrency { get; init; } = Currency.Cop;

    /// <summary>The launch language (decision 8 of the SRS), used when nothing else says otherwise.</summary>
    public const string FallbackLanguage = "es";

    public string DefaultLanguage { get; init; } = FallbackLanguage;

    public string DefaultCountry { get; init; } = "CO";

    public TimeSpan EmailVerificationTokenLifetime { get; init; } = TimeSpan.FromHours(24);

    /// <summary>A new verification email is not sent if the last one is younger than this (R4.4).</summary>
    public TimeSpan VerificationResendCooldown { get; init; } = TimeSpan.FromSeconds(60);
}
