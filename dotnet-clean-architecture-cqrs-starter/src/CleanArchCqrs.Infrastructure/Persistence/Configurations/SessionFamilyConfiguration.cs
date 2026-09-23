using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class SessionFamilyConfiguration : IEntityTypeConfiguration<SessionFamily>
{
    public void Configure(EntityTypeBuilder<SessionFamily> builder)
    {
        builder.ToTable("SessionFamilies");
        builder.Property(f => f.Id).ValueGeneratedNever();
        // Lưu dạng chuỗi: câu lệnh khoá raw SQL ở SessionRepository so sánh "Status" = 'Active'.
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.RevokeReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(f => f.IpAddress).HasMaxLength(64);
        builder.Property(f => f.UserAgent).HasMaxLength(512);
        builder.HasIndex(f => new { f.UserId, f.Status });
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(f => f.Tokens).WithOne().HasForeignKey(t => t.FamilyId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(f => f.DomainEvents);
    }
}
