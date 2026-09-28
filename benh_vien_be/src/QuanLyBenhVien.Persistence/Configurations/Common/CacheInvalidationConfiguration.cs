using QuanLyBenhVien.Persistence.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace QuanLyBenhVien.Persistence.Configurations.Common;

internal sealed class CacheInvalidationConfiguration : IEntityTypeConfiguration<CacheInvalidation>
{
    public void Configure(EntityTypeBuilder<CacheInvalidation> builder)
    {
        builder.ToTable("CacheInvalidations");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Key).IsRequired().HasMaxLength(200);
        builder.Property(c => c.LastError).HasMaxLength(1000);
        builder.HasIndex(c => c.CreatedAtUtc);
    }
}
