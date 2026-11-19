using MiSaldo.Domain.Periods;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class FinancialPeriodConfiguration : IEntityTypeConfiguration<FinancialPeriod>
{
    public void Configure(EntityTypeBuilder<FinancialPeriod> builder)
    {
        builder.ToTable("financial_periods");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.StartDate).HasColumnType("date");
        builder.Property(p => p.EndDate).HasColumnType("date");
        builder.Property(p => p.IsClosed);
        builder.Property(p => p.CarryOverEnabled);
        // Figures frozen when the period is closed (RN-P04); null while it is open.
        builder.Property(p => p.ClosingBalance).HasColumnType("numeric(18,2)");
        builder.Property(p => p.CarriedOver).HasColumnType("numeric(18,2)");

        // End date must not precede start date (RNF-DAT-05, R5.5).
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_period_end_after_start", "\"EndDate\" >= \"StartDate\""));

        FinancialEntityConfig.AddUserScopedFields(builder);
    }
}
