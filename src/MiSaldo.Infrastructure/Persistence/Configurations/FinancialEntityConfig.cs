using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Applies the fields every financial entity shares (sección 7 del SRS):
/// <c>UserId</c>, <c>CreatedAt</c>, <c>UpdatedAt</c> and a row-version
/// concurrency token. These are shadow properties so the domain entities stay
/// free of persistence concerns (RNF-MAN-01). <c>UserId</c> is indexed for
/// per-user filtering (RNF-SEG-04, RNF-REN-01) and the concurrency token uses
/// PostgreSQL's system column <c>xmin</c> (RNF-DAT-04).
/// </summary>
internal static class FinancialEntityConfig
{
    public static void AddUserScopedFields<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<Guid>("UserId").IsRequired();
        builder.HasIndex("UserId");

        builder.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamptz");
        builder.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamptz");

        // PostgreSQL optimistic concurrency via the xmin system column.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
