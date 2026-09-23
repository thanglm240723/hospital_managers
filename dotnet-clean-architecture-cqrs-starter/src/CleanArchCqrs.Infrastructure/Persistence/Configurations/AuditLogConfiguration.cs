using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Changes).IsRequired().HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.ChangedAt });
        builder.HasIndex(a => new { a.ChangedByUserId, a.ChangedAt });
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
    }
}
