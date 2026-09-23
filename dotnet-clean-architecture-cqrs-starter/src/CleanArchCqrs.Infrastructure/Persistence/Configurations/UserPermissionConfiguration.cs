using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions");
        builder.HasKey(p => new { p.UserId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(100);
        builder.Property(p => p.Reason).IsRequired().HasMaxLength(500);
    }
}
