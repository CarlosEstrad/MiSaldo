using MiSaldo.Domain.Expenses;
using MiSaldo.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.CategoryId).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Date).HasColumnType("date");

        builder.MapMoney(e => e.Budgeted, "budgeted");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_expense_budget_positive", "budgeted_amount > 0"));

        // Map the read-only payments collection through its backing field so the
        // aggregate keeps its encapsulation (domain exposes IReadOnlyList).
        builder.HasMany(e => e.Payments)
            .WithOne()
            .HasForeignKey("ExpenseId");

        builder.Navigation(e => e.Payments)
            .HasField("_payments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        FinancialEntityConfig.AddUserScopedFields(builder);
    }
}
