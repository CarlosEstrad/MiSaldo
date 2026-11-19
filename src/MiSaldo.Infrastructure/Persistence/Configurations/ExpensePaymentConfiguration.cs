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

        builder.MapMoney(p => p.Value, "value");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_expense_payment_value_positive", "value_amount > 0"));

        // Foreign key to the owning expense (shadow property set by the Expense config).
        builder.Property<Guid>("ExpenseId");
        builder.HasIndex("ExpenseId");

        builder.Property<string>("IdempotencyKey").HasMaxLength(128);

        // Adds the UserId/CreatedAt/UpdatedAt and concurrency shadow fields;
        // the idempotency index below depends on UserId existing.
        FinancialEntityConfig.AddUserScopedFields(builder);

        // Idempotency key: the same payment sent twice with the same key must be
        // a single row (RNF-DAT-03). A partial unique index over
        // (UserId, IdempotencyKey) enforces it only when a key is present, so
        // payments without a key are unaffected.
        builder.HasIndex("UserId", "IdempotencyKey")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
