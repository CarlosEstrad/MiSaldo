using MiSaldo.Domain.Expenses;
using MiSaldo.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class ExpensePaymentConfiguration : IEntityTypeConfiguration<ExpensePayment>
{
    public void Configure(EntityTypeBuilder<ExpensePayment> builder)
    {
        builder.ToTable("expense_payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Date).HasColumnType("date");

        builder.Property(p => p.Value)
            .HasConversion(MoneyConverter.Cop)
            .HasColumnName("value_amount")
            .HasColumnType("numeric(18,2)");

        // Foreign key to the owning expense (shadow property set by the Expense config).
        builder.Property<Guid>("ExpenseId");
        builder.HasIndex("ExpenseId");

        // Idempotency key: the same payment sent twice with the same key is one row (RNF-DAT-03).
        builder.Property<string>("IdempotencyKey").HasMaxLength(128);

        FinancialEntityConfig.AddUserScopedFields(builder);
    }
}
