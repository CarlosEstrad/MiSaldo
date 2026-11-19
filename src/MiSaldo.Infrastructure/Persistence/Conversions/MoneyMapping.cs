using MiSaldo.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace MiSaldo.Infrastructure.Persistence.Conversions;

/// <summary>
/// Maps a <see cref="Money"/> property to two columns: <c>*_amount</c>
/// (<c>numeric(18,2)</c>) and <c>*_currency</c> (ISO code), so each stored
/// amount keeps its own currency (design T2, RNF-DAT-02). The domain stays free
/// of EF Core: the mapping lives here.
/// </summary>
public static class MoneyMapping
{
    /// <summary>Stores a <see cref="Currency"/> as its ISO code and rebuilds it with its decimal places.</summary>
    public static ValueConverter<Currency, string> CurrencyConverter { get; } = new(
        currency => currency.Code,
        code => FromCode(code));

    // Converters are expression trees, which cannot hold a throw; the check lives in a method.
    private static Currency FromCode(string code) =>
        Currency.FindByCode(code) ?? throw new InvalidOperationException($"Unsupported currency '{code}' in the database.");

    public static void MapMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money>> property,
        string columnPrefix)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ComplexProperty(property, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName($"{columnPrefix}_amount")
                .HasColumnType("numeric(18,2)");
            money.Property(m => m.Currency)
                .HasConversion(CurrencyConverter)
                .HasColumnName($"{columnPrefix}_currency")
                .HasMaxLength(3)
                .HasDefaultValue(Currency.Cop);
        });
    }
}
