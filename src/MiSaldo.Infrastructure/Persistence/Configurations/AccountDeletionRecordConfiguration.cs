using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MiSaldo.Infrastructure.Persistence.Configurations;

public sealed class AccountDeletionRecordConfiguration : IEntityTypeConfiguration<AccountDeletionRecord>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRecord> builder)
    {
        builder.ToTable("account_deletion_records");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Reason).HasMaxLength(512).IsRequired();
        builder.Property(r => r.PerformedByUserId).IsRequired();
        builder.Property(r => r.DeletedAt).HasColumnType("timestamptz");
    }
}
