using QuanLyBenhVien.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace QuanLyBenhVien.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions");
        builder.HasKey(p => new { p.UserId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(100);
        builder.Property(p => p.Reason).IsRequired().HasMaxLength(500);
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionCode).OnDelete(DeleteBehavior.Restrict);
    }
}
