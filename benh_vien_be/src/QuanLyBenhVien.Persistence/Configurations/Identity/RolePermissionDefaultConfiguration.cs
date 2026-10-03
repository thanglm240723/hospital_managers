using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Seed;

namespace QuanLyBenhVien.Persistence.Configurations.Identity;

internal sealed class RolePermissionDefaultConfiguration : IEntityTypeConfiguration<RolePermissionDefault>
{
    public void Configure(EntityTypeBuilder<RolePermissionDefault> builder)
    {
        builder.ToTable("RolePermissionDefaults");
        builder.HasKey(x => new { x.RoleId, x.PermissionCode });
        builder.Property(x => x.PermissionCode).HasMaxLength(100);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}
