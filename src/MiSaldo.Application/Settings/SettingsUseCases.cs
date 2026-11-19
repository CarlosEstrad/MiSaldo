using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;
namespace MiSaldo.Application.Settings;

/// <summary>The user's preferences, stored on the server so every device sees the same (RF-CFG-02).</summary>
public sealed record SettingsDto(
    string PrimaryCurrency,
    string Language,
    ThemePreference Theme,
    PeriodType DefaultPeriodType,
    bool CarryOverEnabled,
    DayOfWeek WeekStart,
    int BiweeklySecondCycleStartDay,
    int MonthlyStartDay);

/// <summary>Replaces every preference at once (RF-CFG-01 to RF-CFG-06, RF-PER-04).</summary>
public sealed record UpdateSettingsCommand(
    string PrimaryCurrency,
    string Language,
    ThemePreference Theme,
    PeriodType DefaultPeriodType,
    bool CarryOverEnabled,
    DayOfWeek WeekStart,
    int BiweeklySecondCycleStartDay,
    int MonthlyStartDay);

/// <summary>What the settings accept: the F1 languages and currencies (RF-CFG-03, RN-M07).</summary>
public static class SupportedSettings
{
    public static IReadOnlyList<string> Languages { get; } = ["es", "en"];

    public static IReadOnlyList<Currency> Currencies { get; } = [Currency.Cop, Currency.Usd];

    public static Currency? FindCurrency(string? code) =>
        Currencies.FirstOrDefault(c => string.Equals(c.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the current user's settings.</summary>
public sealed class GetSettingsHandler
{
    public const string NotFoundCode = "settings.not_found";

    private readonly IUserRepository _users;
    private readonly ICurrentUserAccessor _currentUser;

    public GetSettingsHandler(IUserRepository users, ICurrentUserAccessor currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<SettingsDto>> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<SettingsDto>(CurrentUserErrors.NoUser);
        }

        var settings = await _users.GetSettingsAsync(userId, cancellationToken);
        return settings is null
            ? Result.Failure<SettingsDto>(Error.NotFound(NotFoundCode, "The settings were not found."))
            : Result.Success(ToDto(settings));
    }

    internal static SettingsDto ToDto(UserSettings settings) => new(
        settings.PrimaryCurrency.Code,
        settings.Language,
        settings.Theme,
        settings.DefaultPeriodType,
        settings.CarryOverEnabled,
        settings.PeriodSettings.WeekStart,
        settings.PeriodSettings.BiweeklySecondCycleStartDay,
        settings.PeriodSettings.MonthlyStartDay);
}

/// <summary>
/// Updates the preferences. The primary currency can only change while the
/// user has no movements (RF-PER-04, D-07). New period settings only affect
/// periods created afterwards (RF-PRD-04).
/// </summary>
public sealed class UpdateSettingsHandler
{
    public const string InvalidLanguageCode = "settings.invalid_language";
    public const string UnsupportedCurrencyCode = "settings.unsupported_currency";
    public const string InvalidOptionCode = "settings.invalid_option";
    public const string InvalidPeriodDayCode = "settings.invalid_period_day";
    public const string CurrencyLockedCode = "settings.currency_locked";

    private readonly IUserRepository _users;
    private readonly IIncomeRepository _incomes;
    private readonly IExpenseRepository _expenses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUser;

    public UpdateSettingsHandler(
        IUserRepository users,
        IIncomeRepository incomes,
        IExpenseRepository expenses,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        _users = users;
        _incomes = incomes;
        _expenses = expenses;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<SettingsDto>> HandleAsync(UpdateSettingsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is not { } userId)
        {
            return Result.Failure<SettingsDto>(CurrentUserErrors.NoUser);
        }

        var language = command.Language?.Trim().ToLowerInvariant();
        if (language is null || !SupportedSettings.Languages.Contains(language))
        {
            return Result.Failure<SettingsDto>(Error.Validation(
                InvalidLanguageCode, $"The language must be one of: {string.Join(", ", SupportedSettings.Languages)}."));
        }

        if (SupportedSettings.FindCurrency(command.PrimaryCurrency) is not { } currency)
        {
            return Result.Failure<SettingsDto>(Error.Validation(
                UnsupportedCurrencyCode,
                $"The currency must be one of: {string.Join(", ", SupportedSettings.Currencies.Select(c => c.Code))}."));
        }

        if (!Enum.IsDefined(command.Theme) || !Enum.IsDefined(command.DefaultPeriodType) || !Enum.IsDefined(command.WeekStart))
        {
            return Result.Failure<SettingsDto>(Error.Validation(InvalidOptionCode, "One of the options is not valid."));
        }

        // Checked here so a bad day is a 400, not an exception from the domain type.
        if (command.BiweeklySecondCycleStartDay is < 2 or > 28 || command.MonthlyStartDay is < 1 or > 28)
        {
            return Result.Failure<SettingsDto>(Error.Validation(
                InvalidPeriodDayCode,
                "The second biweekly cycle must start between day 2 and 28, and the month between day 1 and 28."));
        }

        var settings = await _users.GetSettingsAsync(userId, cancellationToken);
        if (settings is null)
        {
            return Result.Failure<SettingsDto>(Error.NotFound(GetSettingsHandler.NotFoundCode, "The settings were not found."));
        }

        if (currency != settings.PrimaryCurrency)
        {
            var hasMovements = await _incomes.HasAnyAsync(cancellationToken)
                || await _expenses.HasAnyAsync(cancellationToken);
            if (hasMovements)
            {
                return Result.Failure<SettingsDto>(Error.Conflict(
                    CurrencyLockedCode, "The primary currency cannot change once there are movements."));
            }

            settings.ChangePrimaryCurrency(currency, hasMovements: false);
        }

        settings.UpdatePreferences(language, command.Theme, command.CarryOverEnabled);
        settings.UpdatePeriodConfiguration(
            command.DefaultPeriodType,
            new PeriodSettings(command.WeekStart, command.BiweeklySecondCycleStartDay, command.MonthlyStartDay));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(GetSettingsHandler.ToDto(settings));
    }
}
