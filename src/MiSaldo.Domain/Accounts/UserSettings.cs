using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;

namespace MiSaldo.Domain.Accounts;

/// <summary>
/// A user's personal and financial preferences, stored on the server so they
/// apply on any device (RF-CFG-02 to RF-CFG-06). The primary currency is locked
/// once the user has movements (RF-PER-04, D-07); that rule is enforced where
/// movements are known, this type only guards the change entry point.
/// </summary>
public sealed class UserSettings
{
    // Parameterless constructor for ORM materialization. The ORM sets the
    // mapped properties, including the PeriodSettings complex value, after
    // construction; application code uses the public constructor below.
    private UserSettings()
    {
        PrimaryCurrency = Currency.Cop;
        Language = string.Empty;
        PeriodSettings = new PeriodSettings();
    }

    public UserSettings(
        Guid id,
        Guid userId,
        Currency primaryCurrency,
        string language,
        ThemePreference theme,
        PeriodType defaultPeriodType,
        PeriodSettings periodSettings,
        bool carryOverEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(primaryCurrency);
        ArgumentNullException.ThrowIfNull(periodSettings);

        Id = id;
        UserId = userId;
        PrimaryCurrency = primaryCurrency;
        Language = language;
        Theme = theme;
        DefaultPeriodType = defaultPeriodType;
        PeriodSettings = periodSettings;
        CarryOverEnabled = carryOverEnabled;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public Currency PrimaryCurrency { get; private set; }

    public string Language { get; private set; }

    public ThemePreference Theme { get; private set; }

    public PeriodType DefaultPeriodType { get; private set; }

    public PeriodSettings PeriodSettings { get; private set; }

    public bool CarryOverEnabled { get; private set; }

    public void UpdatePreferences(string language, ThemePreference theme, bool carryOverEnabled)
    {
        Language = language;
        Theme = theme;
        CarryOverEnabled = carryOverEnabled;
    }

    public void UpdatePeriodConfiguration(PeriodType defaultPeriodType, PeriodSettings periodSettings)
    {
        ArgumentNullException.ThrowIfNull(periodSettings);
        DefaultPeriodType = defaultPeriodType;
        PeriodSettings = periodSettings;
    }

    /// <summary>
    /// Changes the primary currency. Only allowed when the user has no movements
    /// yet; the caller passes whether movements exist (RF-PER-04, D-07).
    /// </summary>
    public void ChangePrimaryCurrency(Currency newCurrency, bool hasMovements)
    {
        ArgumentNullException.ThrowIfNull(newCurrency);
        if (hasMovements)
        {
            throw new InvalidOperationException(
                "Primary currency cannot be changed once the user has movements.");
        }

        PrimaryCurrency = newCurrency;
    }
}
