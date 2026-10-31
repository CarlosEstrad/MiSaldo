using MiSaldo.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MiSaldo.Infrastructure.Persistence.Conversions;

/// <summary>
/// Converts <see cref="Money"/> to and from a <c>numeric(18,2)</c> column. The
/// amount is stored as a decimal; the currency is reconstructed from the
/// configured currency on materialization. In F1 each user has a single primary
/// currency, so the amount column plus that currency fully describes the value
/// (design T2, RNF-DAT-02). Keeping the conversion here means the domain stays
/// free of EF Core dependencies (RNF-MAN-01).
/// </summary>
public sealed class MoneyConverter : ValueConverter<Money, decimal>
{
    public MoneyConverter(Currency currency)
        : base(
            money => money.Amount,
            amount => new Money(amount, currency))
    {
    }

    /// <summary>Converter for the launch currency (COP). Used until per-user currency wiring lands.</summary>
    public static MoneyConverter Cop { get; } = new(Currency.Cop);
}
