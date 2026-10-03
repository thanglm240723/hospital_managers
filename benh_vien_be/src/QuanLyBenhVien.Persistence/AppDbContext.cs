using QuanLyBenhVien.Domain.Catalog.Facilities;
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
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<QuanLyBenhVien.Domain.Identity.Staff.StaffProfile> StaffProfiles => Set<QuanLyBenhVien.Domain.Identity.Staff.StaffProfile>();
    public DbSet<CacheInvalidation> CacheInvalidations => Set<CacheInvalidation>();
    internal DbSet<QuanLyBenhVien.Persistence.Seed.RolePermissionDefault> RolePermissionDefaults => Set<QuanLyBenhVien.Persistence.Seed.RolePermissionDefault>();

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => new AppDbTransaction(await Database.BeginTransactionAsync(ct));

    /// Chỉ dịch những unique constraint mà Application cần phân biệt (mỗi cái có use case xử lý riêng);
    /// các vi phạm khác giữ nguyên DbUpdateException.
    private static class TranslatedUniqueConstraints
    {
        public const string Roles = "IX_Roles_Code";
        public const string UserEmail = "IX_Users_Email";
        public const string Branches = "IX_Branches_Code";
        public const string Departments = "IX_Departments_BranchId_Code";
        public const string Rooms = "IX_Rooms_DepartmentId_Code";
        public const string StaffCode = "IX_StaffProfiles_StaffCode";
        public const string StaffUserId = "IX_StaffProfiles_UserId";
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new QuanLyBenhVien.Domain.Exceptions.ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: TranslatedUniqueConstraints.Roles or TranslatedUniqueConstraints.UserEmail or TranslatedUniqueConstraints.Branches or TranslatedUniqueConstraints.Departments or TranslatedUniqueConstraints.Rooms or TranslatedUniqueConstraints.StaffCode or TranslatedUniqueConstraints.StaffUserId } pg)
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
