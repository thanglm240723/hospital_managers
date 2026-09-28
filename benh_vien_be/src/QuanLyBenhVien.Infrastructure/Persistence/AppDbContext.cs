using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<SessionFamily> SessionFamilies => Set<SessionFamily>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<CacheInvalidation> CacheInvalidations => Set<CacheInvalidation>();

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => new AppDbTransaction(await Database.BeginTransactionAsync(ct));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
