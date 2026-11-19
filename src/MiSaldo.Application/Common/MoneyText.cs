using System.Globalization;
using MiSaldo.Domain.Common;
namespace MiSaldo.Application.Common;

/// <summary>
/// Amounts travel as decimal strings with the currency's decimal places
/// ("150000" for COP, "12.50" for USD), never as binary floating point
/// (RNF-DAT-02).
/// </summary>
public static class MoneyText
{
    public static string Format(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        var rounded = new Money(amount, currency).Round().Amount;
        return rounded.ToString("F" + currency.DecimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    public static string Format(Money money) => Format(money.Amount, money.Currency);
}
