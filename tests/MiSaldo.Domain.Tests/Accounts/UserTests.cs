using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;
using Xunit;

namespace MiSaldo.Domain.Tests.Accounts;

public class UserTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    // CP-11: registering with a birth date under 18 is rejected; no account created.
    [Fact]
    public void Cp11_under_18_registration_is_rejected()
    {
        var birthDate = new DateOnly(2010, 1, 16); // turns 16 the day after "today"
        var ex = Assert.Throws<UnderageUserException>(() =>
            User.Register(Guid.NewGuid(), "cognito-sub-1", birthDate, Today));

        Assert.Equal(18, ex.MinimumAge);
        Assert.True(ex.AgeYears < 18);
    }

    [Fact]
    public void Exactly_18_today_is_accepted()
    {
        var birthDate = Today.AddYears(-18); // turns 18 exactly today
        var user = User.Register(Guid.NewGuid(), "cognito-sub-2", birthDate, Today);
        Assert.Equal(AccountType.Standard, user.AccountType);
        Assert.Null(user.GuardianUserId);
    }

    [Fact]
    public void One_day_short_of_18_is_rejected()
    {
        var birthDate = Today.AddYears(-18).AddDays(1); // turns 18 tomorrow
        Assert.Throws<UnderageUserException>(() =>
            User.Register(Guid.NewGuid(), "cognito-sub-3", birthDate, Today));
    }

    [Fact]
    public void Blank_external_id_is_rejected()
    {
        var birthDate = new DateOnly(1990, 1, 1);
        Assert.Throws<ArgumentException>(() =>
            User.Register(Guid.NewGuid(), "  ", birthDate, Today));
    }

    [Theory]
    [InlineData(1990, 1, 1, 36)]
    [InlineData(2008, 1, 15, 18)]
    [InlineData(2008, 1, 16, 17)]
    public void Age_on_computes_whole_years(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, User.AgeOn(new DateOnly(y, m, d), Today));
    }

    [Fact]
    public void Currency_change_is_blocked_once_movements_exist()
    {
        var settings = new UserSettings(
            Guid.NewGuid(), Guid.NewGuid(), Currency.Cop, "es",
            ThemePreference.System, PeriodType.Monthly, new PeriodSettings());

        Assert.Throws<InvalidOperationException>(() =>
            settings.ChangePrimaryCurrency(Currency.Usd, hasMovements: true));

        settings.ChangePrimaryCurrency(Currency.Usd, hasMovements: false);
        Assert.Equal(Currency.Usd, settings.PrimaryCurrency);
    }
}
