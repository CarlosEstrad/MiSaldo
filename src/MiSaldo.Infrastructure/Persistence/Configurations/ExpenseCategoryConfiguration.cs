using MiSaldo.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("expense_categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(128).IsRequired();
        builder.Property(c => c.SortOrder);
        builder.Property(c => c.IsArchived);

        FinancialEntityConfig.AddUserScopedFields(builder);

        // Unique category name per user, case-insensitive (RF-CAT-04). The
        // case-insensitive expression index is added in the migration task.
        builder.HasIndex("UserId", nameof(ExpenseCategory.Name)).IsUnique();
    }
}
