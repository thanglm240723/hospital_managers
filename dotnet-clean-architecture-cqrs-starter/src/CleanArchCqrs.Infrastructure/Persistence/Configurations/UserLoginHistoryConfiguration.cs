using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserLoginHistoryConfiguration : IEntityTypeConfiguration<UserLoginHistory>
{
    public void Configure(EntityTypeBuilder<UserLoginHistory> builder)
    {
        builder.Property(h => h.EmailAttempted).IsRequired().HasMaxLength(256);
        builder.Property(h => h.FailureReason).HasMaxLength(100);
        builder.Property(h => h.IpAddress).HasMaxLength(64);
        builder.Property(h => h.UserAgent).HasMaxLength(512);
        builder.HasIndex(h => new { h.UserId, h.AttemptedAt });
        builder.HasIndex(h => new { h.EmailAttempted, h.AttemptedAt });
    }
}
