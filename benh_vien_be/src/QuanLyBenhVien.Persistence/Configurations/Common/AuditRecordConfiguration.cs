using QuanLyBenhVien.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace QuanLyBenhVien.Persistence.Configurations.Common;

internal sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(200);
        builder.Property(a => a.ResourceType).HasMaxLength(100);
        builder.Property(a => a.ResourceId).HasMaxLength(100);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.Metadata).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.ActorId, a.TimestampUtc });
        builder.HasIndex(a => new { a.Action, a.TimestampUtc });
    }
}
