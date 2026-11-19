using System.Globalization;
using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;
namespace MiSaldo.Application.Movements;

/// <summary>
/// Reads an amount sent by the client as a decimal string ("150000",
/// "12.50"), never as a float (RNF-DAT-02). It must be positive (RF-ING-04,
/// R12.4), fit <c>numeric(18,2)</c> and use no more decimals than the currency
/// allows, so nothing is rounded silently (RN-M07).
/// </summary>
public static class AmountParser
{
    public const string InvalidCode = "amount.invalid";
    public const string NotPositiveCode = "amount.not_positive";
    public const string TooManyDecimalsCode = "amount.too_many_decimals";
    public const string TooLargeCode = "amount.too_large";

    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public static Result<Money> Parse(string? text, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            return Result.Failure<Money>(Error.Validation(InvalidCode, "The amount must be a decimal number such as 150000 or 12.50."));
        }

        if (amount <= 0)
        {
            return Result.Failure<Money>(Error.Validation(NotPositiveCode, "The amount must be greater than zero."));
        }

        if (amount.Scale > currency.DecimalPlaces && amount != Math.Round(amount, currency.DecimalPlaces))
        {
            return Result.Failure<Money>(Error.Validation(
                TooManyDecimalsCode, $"{currency.Code} amounts have at most {currency.DecimalPlaces} decimal places."));
        }

        if (amount > MaxAmount)
        {
            return Result.Failure<Money>(Error.Validation(TooLargeCode, "The amount is too large."));
        }

        return Result.Success(new Money(amount, currency));
    }
}

/// <summary>Checks the free-text fields of a movement against the column limits.</summary>
internal static class TextField
{
    public static Error? Validate(string? value, string code, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength
            ? Error.Validation(code, $"The {name} is required and must have at most {maxLength} characters.")
            : null;
}

/// <summary>
/// Decides whether a movement may be written on some dates. A closed period
/// keeps its figures (RN-P04), and nothing after an overdue period may change
/// until that period is closed (RF-PRD-06b, CP-21).
/// </summary>
public sealed class PeriodWriteRules
{
    public const string ClosedCode = "period.closed";

    private readonly IPeriodRepository _periods;
    private readonly IClock _clock;
    private readonly RegistrationOptions _options;

    public PeriodWriteRules(IPeriodRepository periods, IClock clock, RegistrationOptions options)
    {
        _periods = periods;
        _clock = clock;
        _options = options;
    }

    /// <summary>"Today" in the launch zone; settings have no time zone yet.</summary>
    public DateOnly Today => _clock.Today(_options.TimeZone);

    public async Task<Error?> CheckAsync(IEnumerable<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        var periods = await _periods.ListAsync(cancellationToken);
        var today = Today;
        foreach (var date in dates.Distinct())
        {
            if (periods.Any(p => p.IsClosed && p.Contains(date)))
            {
                return Error.Unprocessable(ClosedCode, "The period of this date is closed; its figures can no longer change.");
            }

            if (periods.Any(p => p.StatusOn(today) == PeriodStatus.Overdue && p.EndDate < date))
            {
                return Error.Unprocessable(
                    Periods.PeriodHandlers.OverduePendingCode, "Close the overdue period before changing a later one.");
            }
        }

        return null;
    }
}

/// <summary>Loads the current user's settings, which give the currency of new movements.</summary>
internal static class CurrentSettings
{
    public static async Task<UserSettings?> LoadAsync(
        ICurrentUserAccessor currentUser, IUserRepository users, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId ? await users.GetSettingsAsync(userId, cancellationToken) : null;
}
