using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Application.Movements;
using MiSaldo.Application.Periods;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Engine;
namespace MiSaldo.Application.Dashboard;

/// <summary>
/// The indicators of one period, straight from <see cref="FinancialEngine"/>:
/// the API never recomputes them (RNF-MAN-02, R13.3). Amounts are decimal
/// strings; free money is shown as it is, negative included (RF-DSH-09, RN-F09).
/// </summary>
public sealed record DashboardDto(
    PeriodDto Period,
    string Currency,
    string ReceivedIncome,
    string PaidExpense,
    string OutstandingObligations,
    string CarryOver,
    string CashOnHand,
    string Reserved,
    string FreeMoney,
    bool FreeMoneyIsNegative,
    decimal BudgetConsumption);

/// <summary>Builds the dashboard of a period: the one asked for, or the one containing today.</summary>
public sealed class GetDashboardHandler
{
    private readonly PeriodHandlers _periods;
    private readonly IIncomeRepository _incomes;
    private readonly IExpenseRepository _expenses;
    private readonly IPeriodRepository _periodRepository;
    private readonly IUserRepository _users;
    private readonly ICurrentUserAccessor _currentUser;

    public GetDashboardHandler(
        PeriodHandlers periods,
        IIncomeRepository incomes,
        IExpenseRepository expenses,
        IPeriodRepository periodRepository,
        IUserRepository users,
        ICurrentUserAccessor currentUser)
    {
        _periods = periods;
        _incomes = incomes;
        _expenses = expenses;
        _periodRepository = periodRepository;
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<DashboardDto>> HandleAsync(Guid? periodId, CancellationToken cancellationToken = default)
    {
        if (await CurrentSettings.LoadAsync(_currentUser, _users, cancellationToken) is not { } settings)
        {
            return Result.Failure<DashboardDto>(CurrentUserErrors.NoUser);
        }

        // Without an id, the period of today; a day without a period is a 404, never a new period (CP-20).
        var periodResult = periodId is { } id
            ? await _periods.GetAsync(id, cancellationToken)
            : await _periods.GetForDateAsync(null, cancellationToken);
        if (periodResult.IsFailure)
        {
            return Result.Failure<DashboardDto>(periodResult.Error!);
        }

        var dto = periodResult.Value;
        var periods = await _periodRepository.ListAsync(cancellationToken);
        var period = periods.Single(p => p.Id == dto.Id);
        var currency = settings.PrimaryCurrency;

        var indicators = FinancialEngine.Compute(new EngineInput
        {
            Period = period,
            Currency = currency,
            CarryOver = new Money(PeriodHandlers.OpeningBalance(period, periods), currency),
            Incomes = await _incomes.ListForRangeAsync(period.StartDate, period.EndDate, cancellationToken),
            Expenses = await _expenses.ListForRangeAsync(period.StartDate, period.EndDate, cancellationToken),
        });

        return Result.Success(new DashboardDto(
            dto,
            currency.Code,
            MoneyText.Format(indicators.ReceivedIncome),
            MoneyText.Format(indicators.PaidExpense),
            MoneyText.Format(indicators.OutstandingObligations),
            MoneyText.Format(indicators.CarryOver),
            MoneyText.Format(indicators.CashOnHand),
            MoneyText.Format(indicators.Reserved),
            MoneyText.Format(indicators.FreeMoney),
            indicators.FreeMoney.IsNegative,
            // A ratio, not money: four decimals are enough to show a percentage with two.
            Math.Round(indicators.BudgetConsumption, 4, MidpointRounding.ToEven)));
    }
}
