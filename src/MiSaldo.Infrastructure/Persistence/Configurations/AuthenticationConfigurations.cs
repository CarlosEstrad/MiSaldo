using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

/// <summary>
/// Authentication tables are account-level data, not per-user financial data:
/// they are looked up by token hash or by user id, so they are not covered by the
/// per-user query filter or Row Level Security. Each carries an <c>xmin</c>
/// concurrency token, so two requests racing to use the same token cannot both win.
/// </summary>
public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId).IsRequired();
        builder.HasIndex(c => c.UserId).IsUnique();

        builder.Property(c => c.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(c => c.EmailVerified);
        builder.Property(c => c.FailedLoginAttempts);
        builder.Property(c => c.LockedUntil).HasColumnType("timestamptz");

        builder.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamptz");
        builder.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamptz");
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserCredential>(c => c.UserId);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        builder.HasIndex(t => t.UserId);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz");
        builder.Property(t => t.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(t => t.ConsumedAt).HasColumnType("timestamptz");
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(128);

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId);
    }
}

public sealed class OneTimeTokenConfiguration : IEntityTypeConfiguration<OneTimeToken>
{
    public void Configure(EntityTypeBuilder<OneTimeToken> builder)
    {
        builder.ToTable("one_time_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        builder.HasIndex(t => new { t.UserId, t.Purpose });

        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(32);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz");
        builder.Property(t => t.ExpiresAt).HasColumnType("timestamptz");
        builder.Property(t => t.ConsumedAt).HasColumnType("timestamptz");

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId);
    }
}
