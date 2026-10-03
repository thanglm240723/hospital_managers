using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Staff;

namespace QuanLyBenhVien.Persistence.Configurations.Identity;

internal sealed class StaffProfileConfiguration : IEntityTypeConfiguration<StaffProfile>
{
    public void Configure(EntityTypeBuilder<StaffProfile> builder)
    {
        builder.ToTable("StaffProfiles");
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.StaffCode).IsRequired().HasMaxLength(30);
        builder.HasIndex(p => p.StaffCode).IsUnique().HasDatabaseName("IX_StaffProfiles_StaffCode");
        builder.HasIndex(p => p.UserId).IsUnique().HasDatabaseName("IX_StaffProfiles_UserId");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(p => p.WorkScopes).WithOne().HasForeignKey(s => s.StaffProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.WorkScopes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.DomainEvents);
    }
}
