using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId).IsRequired();
        builder.HasIndex(c => c.UserId);

        builder.Property(c => c.PrivacyPolicyVersion).HasMaxLength(32).IsRequired();
        builder.Property(c => c.TermsVersion).HasMaxLength(32).IsRequired();
        builder.Property(c => c.AcceptedAt).HasColumnType("timestamptz");
    }
}
