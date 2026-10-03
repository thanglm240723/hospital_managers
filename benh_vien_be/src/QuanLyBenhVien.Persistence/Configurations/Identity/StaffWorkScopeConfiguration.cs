using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Identity.Staff;

namespace QuanLyBenhVien.Persistence.Configurations.Identity;

internal sealed class StaffWorkScopeConfiguration : IEntityTypeConfiguration<StaffWorkScope>
{
    public void Configure(EntityTypeBuilder<StaffWorkScope> builder)
    {
        builder.ToTable("StaffWorkScopes");
        builder.HasKey(s => new { s.StaffProfileId, s.DepartmentId });
        builder.HasOne<Department>().WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.BranchId);
    }
}
