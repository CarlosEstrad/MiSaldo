using MiSaldo.Domain.Incomes;
using MiSaldo.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class IncomeTransactionConfiguration : IEntityTypeConfiguration<IncomeTransaction>
{
    public void Configure(EntityTypeBuilder<IncomeTransaction> builder)
    {
        builder.ToTable("income_transactions");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Source).HasMaxLength(128).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.Date).HasColumnType("date");
        builder.Property(i => i.ReceivedDate).HasColumnType("date");

        builder.MapMoney(i => i.Value, "value");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_income_value_positive", "value_amount > 0"));

        FinancialEntityConfig.AddUserScopedFields(builder);
    }
}
