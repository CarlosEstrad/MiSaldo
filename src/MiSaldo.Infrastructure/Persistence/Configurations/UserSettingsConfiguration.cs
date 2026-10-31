using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId).IsRequired();
        builder.HasIndex(s => s.UserId).IsUnique();

        // Currency is stored as its ISO code; the decimal places come from the
        // known currency table, so only the code needs persisting.
        builder.Property(s => s.PrimaryCurrency)
            .HasConversion(
                currency => currency.Code,
                code => CurrencyFromCode(code))
            .HasColumnName("primary_currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(s => s.Language).HasMaxLength(8).IsRequired();
        builder.Property(s => s.Theme).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.DefaultPeriodType).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.CarryOverEnabled);

        // PeriodSettings is a small value object; store its parts as columns.
        builder.ComplexProperty(s => s.PeriodSettings, ps =>
        {
            ps.Property(p => p.WeekStart).HasConversion<string>().HasMaxLength(16);
            ps.Property(p => p.BiweeklySecondCycleStartDay);
            ps.Property(p => p.MonthlyStartDay);
        });

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserSettings>(s => s.UserId);
    }

    private static Currency CurrencyFromCode(string code) => code switch
    {
        "COP" => Currency.Cop,
        "USD" => Currency.Usd,
        _ => new Currency(code, 2),
    };
}
