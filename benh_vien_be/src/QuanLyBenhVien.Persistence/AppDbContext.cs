using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Persistence.Caching;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
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

    /// Chỉ dịch những unique constraint mà Application cần phân biệt (mỗi cái có use case xử lý riêng);
    /// các vi phạm khác giữ nguyên DbUpdateException.
    private static class TranslatedUniqueConstraints
    {
        public const string Roles = "IX_Roles_Code";
        public const string UserEmail = "IX_Users_Email";
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: TranslatedUniqueConstraints.Roles or TranslatedUniqueConstraints.UserEmail } pg)
        {
            throw new QuanLyBenhVien.Domain.Exceptions.UniqueConstraintViolationException(pg.ConstraintName, ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
