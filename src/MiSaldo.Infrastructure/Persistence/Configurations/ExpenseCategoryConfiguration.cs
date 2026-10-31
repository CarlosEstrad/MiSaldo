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

        // The unique category name per user is case-insensitive (RF-CAT-04),
        // implemented as an expression index on lower(Name) via raw SQL in the
        // migration, since EF cannot express it in the model.
    }
}
