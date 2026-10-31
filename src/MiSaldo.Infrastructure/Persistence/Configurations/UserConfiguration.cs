using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.ExternalId).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();

        builder.Property(u => u.BirthDate).HasColumnType("date");
        builder.Property(u => u.AccountType).HasConversion<string>().HasMaxLength(32);
        builder.Property(u => u.GuardianUserId);

        builder.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamptz");
        builder.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamptz");
    }
}
