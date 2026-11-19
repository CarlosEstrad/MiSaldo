using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Engine;
using MiSaldo.Domain.Periods;
namespace MiSaldo.Application.Periods;

/// <summary>
/// A period as the client sees it. <see cref="OpeningBalance"/> is what the
/// previous closed period carried into this one; the closing figures are set
/// only once the period is closed. Amounts are decimal strings (RNF-DAT-02).
/// </summary>
public sealed record PeriodDto(
    Guid Id,
    PeriodType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    PeriodStatus Status,
    bool CarryOverEnabled,
    string OpeningBalance,
    string? ClosingBalance,
    string? CarriedOver);

/// <summary>A suggested range for a new period; nothing is stored (RF-PRD-02, CP-20).</summary>
public sealed record PeriodProposalDto(PeriodType Type, DateOnly StartDate, DateOnly EndDate, bool OverlapsExisting);

/// <summary>
/// Creates a period. Without dates, the range comes from the current settings
/// around <see cref="AnchorDate"/> (today by default); with both dates, the
/// user's adjusted range is used as given (RF-PRD-02).
/// </summary>
public sealed record CreatePeriodCommand(PeriodType? Type, DateOnly? AnchorDate, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>
/// The current user's financial periods (RF-PRD-01 to RF-PRD-06b). Periods are
/// only created on request, never as a side effect of reading (D-01, CP-20).
/// </summary>
public sealed class PeriodHandlers
{
    public const string NotFoundCode = "period.not_found";
    public const string NoneForDateCode = "period.none_for_date";
    public const string InvalidRangeCode = "period.invalid_range";
    public const string OverlapCode = "period.overlap";
    public const string OverduePendingCode = "period.overdue_pending";
    public const string AlreadyClosedCode = "period.already_closed";
    public const string NotStartedCode = "period.not_started";
    public const string PreviousOpenCode = "period.previous_open";

    private readonly IPeriodRepository _periods;
    private readonly IUserRepository _users;
    private readonly IIncomeRepository _incomes;
    private readonly IExpenseRepository _expenses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly RegistrationOptions _options;
    private readonly ICurrentUserAccessor _currentUser;

    public PeriodHandlers(
        IPeriodRepository periods,
        IUserRepository users,
        IIncomeRepository incomes,
        IExpenseRepository expenses,
        IUnitOfWork unitOfWork,
        IClock clock,
        RegistrationOptions options,
        ICurrentUserAccessor currentUser)
    {
        _periods = periods;
        _users = users;
        _incomes = incomes;
        _expenses = expenses;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options;
        _currentUser = currentUser;
    }

    private static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The period was not found.");

    // Settings have no time zone yet, so "today" uses the launch zone, as the age check does.
    private DateOnly Today => _clock.Today(_options.TimeZone);

    public async Task<Result<IReadOnlyList<PeriodDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<IReadOnlyList<PeriodDto>>(CurrentUserErrors.NoUser);
        }

        var periods = await _periods.ListAsync(cancellationToken);
        var today = Today;
        IReadOnlyList<PeriodDto> list = periods
            .Select(p => ToDto(p, OpeningBalance(p, periods), settings.PrimaryCurrency, today))
            .ToList();
        return Result.Success(list);
    }

    public async Task<Result<PeriodDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<PeriodDto>(CurrentUserErrors.NoUser);
        }

        var periods = await _periods.ListAsync(cancellationToken);
        var period = periods.FirstOrDefault(p => p.Id == id);
        return period is null
            ? Result.Failure<PeriodDto>(NotFound)
            : Result.Success(ToDto(period, OpeningBalance(period, periods), settings.PrimaryCurrency, Today));
    }

    /// <summary>The period that contains the date. A date without a period gets a 404, never a new period (CP-20).</summary>
    public async Task<Result<PeriodDto>> GetForDateAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<PeriodDto>(CurrentUserErrors.NoUser);
        }

        var day = date ?? Today;
        var periods = await _periods.ListAsync(cancellationToken);
        var period = periods.FirstOrDefault(p => p.Contains(day));
        return period is null
            ? Result.Failure<PeriodDto>(Error.NotFound(NoneForDateCode, "There is no period for this date. Create one to continue."))
            : Result.Success(ToDto(period, OpeningBalance(period, periods), settings.PrimaryCurrency, Today));
    }

    /// <summary>Suggests the range a new period would have, from the current settings, without storing anything.</summary>
    public async Task<Result<PeriodProposalDto>> ProposeAsync(
        PeriodType? type, DateOnly? anchorDate, CancellationToken cancellationToken = default)
    {
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<PeriodProposalDto>(CurrentUserErrors.NoUser);
        }

        var periodType = type ?? settings.DefaultPeriodType;
        var (start, end) = PeriodCalendar.RangeFor(periodType, anchorDate ?? Today, settings.PeriodSettings);
        var overlaps = await _periods.OverlapsAsync(start, end, cancellationToken);
        return Result.Success(new PeriodProposalDto(periodType, start, end, overlaps));
    }

    public async Task<Result<PeriodDto>> CreateAsync(CreatePeriodCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<PeriodDto>(CurrentUserErrors.NoUser);
        }

        var type = command.Type ?? settings.DefaultPeriodType;
        if (!Enum.IsDefined(type))
        {
            return Result.Failure<PeriodDto>(Error.Validation(InvalidRangeCode, "The period type is not valid."));
        }

        DateOnly start, end;
        if (command.StartDate is { } customStart && command.EndDate is { } customEnd)
        {
            if (customEnd < customStart)
            {
                return Result.Failure<PeriodDto>(Error.Validation(InvalidRangeCode, "The end date cannot be earlier than the start date."));
            }

            (start, end) = (customStart, customEnd);
        }
        else if (command.StartDate is not null || command.EndDate is not null)
        {
            return Result.Failure<PeriodDto>(Error.Validation(
                InvalidRangeCode, "Send both the start and the end date, or neither to use the settings."));
        }
        else
        {
            (start, end) = PeriodCalendar.RangeFor(type, command.AnchorDate ?? Today, settings.PeriodSettings);
        }

        // CP-21: an overdue period must be closed before moving on to a later one (RF-PRD-06b, D-02).
        var overdue = await _periods.ListOverdueAsync(Today, cancellationToken);
        if (overdue.Any(p => p.EndDate < start))
        {
            return Result.Failure<PeriodDto>(Error.Unprocessable(
                OverduePendingCode, "Close the overdue period before starting the next one."));
        }

        var overlap = Error.Conflict(OverlapCode, "The dates overlap an existing period.");
        if (await _periods.OverlapsAsync(start, end, cancellationToken))
        {
            return Result.Failure<PeriodDto>(overlap);
        }

        var period = new FinancialPeriod(Guid.NewGuid(), type, start, end, settings.CarryOverEnabled);
        _periods.Add(period);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // A parallel request created an overlapping period first; the database constraint decided.
            return Result.Failure<PeriodDto>(overlap);
        }

        var periods = await _periods.ListAsync(cancellationToken);
        return Result.Success(ToDto(period, OpeningBalance(period, periods), settings.PrimaryCurrency, Today));
    }

    /// <summary>
    /// Closes the period: computes its final balance with the financial engine,
    /// freezes it and records the carry-over for the next period (RF-PRD-06,
    /// RN-P04, RN-P05, CP-14). Periods close in order, so each one knows its
    /// opening balance.
    /// </summary>
    public async Task<Result<PeriodDto>> CloseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await SettingsAsync(cancellationToken) is not { } settings)
        {
            return Result.Failure<PeriodDto>(CurrentUserErrors.NoUser);
        }

        var periods = await _periods.ListAsync(cancellationToken);
        var period = periods.FirstOrDefault(p => p.Id == id);
        if (period is null)
        {
            return Result.Failure<PeriodDto>(NotFound);
        }

        var today = Today;
        switch (period.StatusOn(today))
        {
            case PeriodStatus.Closed:
                return Result.Failure<PeriodDto>(Error.Conflict(AlreadyClosedCode, "The period is already closed."));
            case PeriodStatus.Planned:
                return Result.Failure<PeriodDto>(Error.Unprocessable(NotStartedCode, "A period that has not started cannot be closed."));
        }

        if (periods.Any(p => p.EndDate < period.StartDate && !p.IsClosed))
        {
            return Result.Failure<PeriodDto>(Error.Unprocessable(
                PreviousOpenCode, "Close the earlier periods first."));
        }

        var currency = settings.PrimaryCurrency;
        var opening = OpeningBalance(period, periods);
        var indicators = FinancialEngine.Compute(new EngineInput
        {
            Period = period,
            Currency = currency,
            CarryOver = new Money(opening, currency),
            Incomes = await _incomes.ListForRangeAsync(period.StartDate, period.EndDate, cancellationToken),
            Expenses = await _expenses.ListForRangeAsync(period.StartDate, period.EndDate, cancellationToken),
        });

        period.Close(indicators.CashOnHand.Round());
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<PeriodDto>(Error.Conflict(AlreadyClosedCode, "The period is already closed."));
        }

        return Result.Success(ToDto(period, opening, currency, today));
    }

    /// <summary>
    /// What the period immediately before this one carried over, if it was
    /// closed; zero otherwise (RN-P05).
    /// </summary>
    internal static decimal OpeningBalance(FinancialPeriod period, IEnumerable<FinancialPeriod> periods)
    {
        var previous = periods
            .Where(p => p.EndDate < period.StartDate)
            .OrderByDescending(p => p.EndDate)
            .FirstOrDefault();
        return previous is { IsClosed: true, CarriedOver: { } carried } ? carried : 0m;
    }

    private async Task<UserSettings?> SettingsAsync(CancellationToken cancellationToken) =>
        _currentUser.UserId is { } userId ? await _users.GetSettingsAsync(userId, cancellationToken) : null;

    private static PeriodDto ToDto(FinancialPeriod period, decimal opening, Currency currency, DateOnly today) => new(
        period.Id,
        period.Type,
        period.StartDate,
        period.EndDate,
        period.StatusOn(today),
        period.CarryOverEnabled,
        MoneyText.Format(opening, currency),
        period.ClosingBalance is { } closing ? MoneyText.Format(closing, currency) : null,
        period.CarriedOver is { } carried ? MoneyText.Format(carried, currency) : null);
}
