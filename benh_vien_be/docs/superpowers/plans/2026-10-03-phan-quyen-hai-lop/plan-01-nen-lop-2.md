# Plan 01 — Nền lớp 2 (phân quyền hai lớp, mốc 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dựng nền cho phân quyền hai lớp: cơ cấu cơ sở/khoa/phòng, hồ sơ nhân sự và phạm vi làm việc, khung `IResourceAuthorizer` + audit từ chối sống qua rollback, seeder gán quyền mặc định một lần, marker chống quên lọc — kèm màn quản trị tương ứng.

**Architecture:** Clean Architecture + CQRS hiện có. Module `Catalog` (Domain/Application/Persistence) sở hữu `Branch`/`Department`/`Room`; module `IdentityAccess` sở hữu `StaffProfile`/`StaffWorkScope`. Port lớp 2 ở `Application/Common/Authorization`, implement ở Persistence. Seeder đọc ma trận trong code, ghi lịch sử áp vào bảng `RolePermissionDefaults`. FE thêm màn `/admin/facilities` và mục "Hồ sơ nhân sự" trong panel tài khoản.

**Tech Stack:** .NET 10, Carter, MediatR 12.5.0, FluentValidation, EF Core 10 + Npgsql (PostgreSQL 17), Redis; xUnit + Testcontainers; React 16, Redux, Jest.

**Spec:** [spec.md](spec.md) — đọc §2, §4.2, §5, §6.1–6.2, §7.1, §7.4, §7.5, §8, §9, §10 (mốc 1) trước mỗi task.

## Global Constraints

- Không có chức năng đặt lịch; không xây module nghiệp vụ lâm sàng trong plan này.
- Mã quyền **kích hoạt trong plan này**: `facilities.read`, `facilities.manage`, `staff-profiles.read`, `staff-profiles.manage`. Không thêm mã nào khác vào `Permissions.cs` (`catalog.*` chờ module danh mục — spec §10).
- Ma trận mặc định viết **đầy đủ** theo spec §5 (kể cả mã chưa kích hoạt); `access-grants.request-emergency` **không** có trong ma trận.
- Seeder: mỗi cặp (vai trò, mã) áp đúng một lần, ghi `RolePermissionDefaults`; advisory lock; invalidation cache quyền trong transaction, `FlushAsync` sau commit. Quyền lõi Admin giữ cơ chế hiện có.
- Khóa `Guid.CreateVersion7()`; thời điểm `DateTimeOffset` UTC lấy từ `TimeProvider`; concurrency `xmin` (`uint RowVersion` + `IsRowVersion()`); mutation trên bản ghi có sẵn nhận `If-Match` (thiếu/sai → 400 `invalid_if_match`, cũ → 412).
- Bảng/cột PascalCase như EF sinh. Migration mới qua `dotnet ef migrations add`, không sửa migration đã commit; nếu EF sinh `AddColumn "xmin"` thì sửa Up/Down của migration **mới** thành không tạo cột (xmin là cột hệ thống) như các migration `RoleRowVersionXmin`/`UserRowVersionXmin`.
- Route `api/v1/<kebab>`, `.WithName("<Tên>V1")`, `RequirePermission(Permissions.X.Y)`, mutation có `.RequireCsrf()`. Thêm route Gateway cho `/api/v1/facilities`.
- Lỗi nghiệp vụ trả `Result.Failure(<Feature>Errors.X)`, mã snake_case, message tiếng Việt. Không ném exception cho lỗi dự kiến.
- Integration test dùng PostgreSQL/Redis thật (Testcontainers), `[Collection(IntegrationCollection.Name)]`; không dùng InMemory/SQLite làm bằng chứng.
- FE: request qua `service/http.js`; action type tiền tố `HMS/`; không lưu dữ liệu vào `localStorage`; không tự retry command; Prettier singleQuote, trailingComma all, printWidth 150.
- Commit từng task trên nhánh làm việc; không push/merge (chờ người dùng). Cuối message: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

Ký hiệu đường dẫn (tính từ `D:/hospital_management`): `BE = benh_vien_be`, `DOM = BE/src/QuanLyBenhVien.Domain`, `APP = BE/src/QuanLyBenhVien.Application`, `PERS = BE/src/QuanLyBenhVien.Persistence`, `PRES = BE/src/QuanLyBenhVien.Presentation`, `API = BE/src/QuanLyBenhVien.API`, `GW = BE/src/QuanLyBenhVien.Gateway`, `UT = BE/tests/QuanLyBenhVien.UnitTests`, `IT = BE/tests/QuanLyBenhVien.IntegrationTests`, `FE = benh_vien_fe/src`.

**Thứ tự thực thi:** Task 1 → 2 → 7 → 3 → 4 → 5 → 6 → 8 → 9 → 10 (Task 7 tạo marker mà Task 3/5 dùng; đánh số giữ theo nhóm chức năng).

Lệnh test (chạy từ `BE`):
- Unit: `dotnet test tests/QuanLyBenhVien.UnitTests --filter "FullyQualifiedName~<Tên>"`
- Integration: `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~<Tên>"` (cần Docker)
- FE (chạy từ `benh_vien_fe`): `CI=true npx react-scripts test --watchAll=false <pattern>` hoặc `node scripts/test.js --watchAll=false <pattern>` theo script hiện có.

---

### Task 1: Ma trận mặc định và seeder "áp một lần"

**Files:**
- Create: `PERS/Seed/DefaultRolePermissions.cs`
- Create: `PERS/Seed/RolePermissionDefault.cs` (entity lịch sử, chỉ dùng ở Persistence)
- Create: `PERS/Configurations/Identity/RolePermissionDefaultConfiguration.cs`
- Create: `PERS/Seed/RoleDefaultsPlan.cs`
- Create: `PERS/Seed/RoleDefaultsApplier.cs`
- Modify: `PERS/Seed/IdentitySeeder.cs` (gọi applier sau `EnsureSystemRolesAsync`)
- Modify: `PERS/AppDbContext.cs` (thêm `DbSet<RolePermissionDefault> RolePermissionDefaults`)
- Create: migration `PERS/Migrations/<ts>_RolePermissionDefaults.cs` (qua `dotnet ef`)
- Test: `UT/Persistence/Seed/DefaultRolePermissionsTests.cs`, `UT/Persistence/Seed/RoleDefaultsPlanTests.cs`, `IT/Admin/RoleDefaultsSeederTests.cs`

**Interfaces:**
- Produces:
  - `internal static class DefaultRolePermissions` với `IReadOnlyList<string> PlannedCodes` (toàn bộ ~70 mã spec §4), `IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole` (khóa = `SystemRoles.*`), `IReadOnlySet<string> Supplementary`.
  - `internal static class RoleDefaultsPlan { static IReadOnlyList<(string RoleCode, string PermissionCode)> PendingPairs(IReadOnlyDictionary<string, IReadOnlyList<string>> matrix, Func<string, bool> isActivated, IReadOnlySet<(string RoleCode, string PermissionCode)> alreadyApplied); }` — hàm thuần, unit test được.
  - `internal sealed class RoleDefaultsApplier { Task ApplyAsync(CancellationToken ct); }`.
  - Bảng `RolePermissionDefaults(RoleId uuid, PermissionCode varchar(100), AppliedAt timestamptz)`, PK `(RoleId, PermissionCode)`, FK `RoleId → Roles` cascade.

- [ ] **Step 1: Viết unit test cho ma trận**

```csharp
// UT/Persistence/Seed/DefaultRolePermissionsTests.cs
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Seed;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Seed;

public class DefaultRolePermissionsTests
{
    [Fact]
    public void Matrix_CoversAllSystemRoles_AndOnlyPlannedCodes()
    {
        Assert.Equal(SystemRoles.All.Select(r => r.Code).Order(), DefaultRolePermissions.ByRole.Keys.Order());
        var planned = DefaultRolePermissions.PlannedCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var (_, codes) in DefaultRolePermissions.ByRole)
            Assert.All(codes, c => Assert.Contains(c, planned));
        Assert.All(DefaultRolePermissions.Supplementary, c => Assert.Contains(c, planned));
    }

    [Fact]
    public void EveryActivatedPermission_HasADefaultDecision()
    {
        var decided = DefaultRolePermissions.ByRole.Values.SelectMany(c => c)
            .Concat(DefaultRolePermissions.Supplementary).ToHashSet(StringComparer.Ordinal);
        // Mã không có vai trò mặc định nào phải nằm trong Supplementary — để không ai quên quyết định gán.
        var undecided = Permissions.All.Select(p => p.Code).Where(c => !decided.Contains(c)).ToList();
        Assert.Empty(undecided);
    }

    [Fact]
    public void EmergencyAccess_IsNotInDefaults() =>
        Assert.DoesNotContain(DefaultRolePermissions.ByRole.Values.SelectMany(c => c), c => c == "access-grants.request-emergency");

    [Fact]
    public void Admin_HasNoClinicalPermission() =>
        Assert.DoesNotContain(DefaultRolePermissions.ByRole[SystemRoles.Admin],
            c => c.StartsWith("encounters.") || c.StartsWith("patient-history.") || c.StartsWith("orders.") || c.StartsWith("results."));
}
```

- [ ] **Step 2: Viết unit test cho `RoleDefaultsPlan.PendingPairs`**

```csharp
// UT/Persistence/Seed/RoleDefaultsPlanTests.cs
using QuanLyBenhVien.Persistence.Seed;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Seed;

public class RoleDefaultsPlanTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Matrix = new()
    {
        ["doctor"] = ["patients.read", "encounters.read"],
        ["receptionist"] = ["patients.read"],
    };

    [Fact]
    public void ReturnsOnlyActivatedAndNotYetAppliedPairs()
    {
        var applied = new HashSet<(string, string)> { ("receptionist", "patients.read") };
        var pending = RoleDefaultsPlan.PendingPairs(Matrix, c => c == "patients.read", applied);
        Assert.Equal([("doctor", "patients.read")], pending);
    }

    [Fact]
    public void NotActivatedCode_IsSkipped()
        => Assert.Empty(RoleDefaultsPlan.PendingPairs(Matrix, _ => false, new HashSet<(string, string)>()));
}
```

- [ ] **Step 3: Chạy để thấy đỏ** — `dotnet test tests/QuanLyBenhVien.UnitTests --filter "FullyQualifiedName~Persistence.Seed"` → FAIL (type không tồn tại). Nếu UT chưa thấy được type `internal` của Persistence, kiểm `InternalsVisibleTo` hiện có trong `PERS/QuanLyBenhVien.Persistence.csproj`; thiếu thì thêm `<InternalsVisibleTo Include="QuanLyBenhVien.UnitTests" />` và `QuanLyBenhVien.IntegrationTests`.

- [ ] **Step 4: Viết `DefaultRolePermissions`** — chép **nguyên văn** mã từ spec §4 vào `PlannedCodes` và ma trận spec §5 vào `ByRole`/`Supplementary`. Mọi vai trò trừ Admin có thêm `catalog.read`, `facilities.read`; Admin có `facilities.read` trong danh sách riêng của mình.

```csharp
// PERS/Seed/DefaultRolePermissions.cs
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Persistence.Seed;

/// Ma trận vai trò × quyền mặc định — spec phân quyền hai lớp §5. Khóa theo chuỗi mã để chứa cả mã CHƯA kích hoạt;
/// seeder chỉ áp mã đã có trong Permissions.All. Sửa ma trận = sửa spec.
internal static class DefaultRolePermissions
{
    public static IReadOnlyList<string> PlannedCodes { get; } =
    [
        "users.read", "users.create", "users.activate", "users.roles.manage", "users.permissions.manage",
        "roles.read", "roles.manage", "permissions.read",
        "facilities.read", "facilities.manage", "staff-profiles.read", "staff-profiles.manage",
        "catalog.read", "catalog.manage", "settings.manage",
        "patients.read", "patients.create", "patients.update",
        "encounters.register", "clinic-sessions.read", "clinic-sessions.manage", "clinic-sessions.staff.assign",
        "queue.read", "queue.call", "queue.transfer", "queue.display",
        "vitals.read", "vitals.record", "encounters.read", "encounters.examine", "encounters.confirm", "encounters.amend",
        "patient-history.read", "orders.read", "orders.create", "orders.cancel",
        "results.read", "results.record", "results.confirm", "prescriptions.read", "prescriptions.create",
        "admission-requests.create", "dispensing.read", "dispensing.record",
        "inpatient.admit", "inpatient.read", "care-team.assign", "beds.read", "beds.assign", "beds.clean", "beds.manage",
        "bed-waitlist.manage", "medical-orders.create", "medical-orders.execute", "inpatient.discharge",
        "billing.read", "billing.collect", "billing.deposit", "billing.refund", "billing.adjust", "billing.settle",
        "billing.reopen", "billing.insurance-rate.override", "billing.emergency-exception.approve",
        "documents.read", "documents.upload", "documents.export",
        "access-grants.request-emergency", "access-grants.approve", "audit.read", "reports.read",
    ];

    private static readonly string[] Common = ["catalog.read", "facilities.read"];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [SystemRoles.Receptionist] = [.. Common, "patients.read", "patients.create", "patients.update", "encounters.register",
            "clinic-sessions.read", "queue.read", "queue.transfer", "inpatient.admit"],
        [SystemRoles.OutpatientNurse] = [.. Common, "patients.read", "clinic-sessions.read", "queue.read", "queue.call",
            "vitals.read", "vitals.record"],
        [SystemRoles.Doctor] = [.. Common, "patients.read", "clinic-sessions.read", "queue.read", "queue.call", "vitals.read",
            "encounters.read", "encounters.examine", "encounters.confirm", "encounters.amend", "patient-history.read",
            "orders.read", "orders.create", "results.read", "prescriptions.read", "prescriptions.create",
            "admission-requests.create", "inpatient.read", "medical-orders.create", "inpatient.discharge",
            "documents.read", "documents.upload", "documents.export"],
        [SystemRoles.InpatientNurse] = [.. Common, "patients.read", "inpatient.read", "beds.read", "beds.assign", "beds.clean",
            "bed-waitlist.manage", "medical-orders.execute", "vitals.read", "vitals.record", "orders.read", "results.read",
            "prescriptions.read", "documents.read", "documents.upload"],
        [SystemRoles.LabTechnician] = [.. Common, "patients.read", "orders.read", "results.read", "results.record",
            "documents.read", "documents.upload"],
        [SystemRoles.Pharmacist] = [.. Common, "patients.read", "prescriptions.read", "dispensing.read", "dispensing.record"],
        [SystemRoles.Cashier] = [.. Common, "patients.read", "billing.read", "billing.collect", "billing.deposit",
            "billing.refund", "billing.settle"],
        [SystemRoles.ClinicalManager] = [.. Common, "patients.read", "clinic-sessions.read", "clinic-sessions.manage",
            "clinic-sessions.staff.assign", "queue.read", "queue.transfer", "care-team.assign", "access-grants.approve",
            "beds.read", "beds.manage", "bed-waitlist.manage", "staff-profiles.read", "audit.read", "reports.read"],
        [SystemRoles.Admin] = [.. Permissions.IdentityAccess.Select(p => p.Code), "facilities.read", "facilities.manage",
            "staff-profiles.read", "staff-profiles.manage", "catalog.read", "catalog.manage", "settings.manage", "audit.read"],
    };

    /// Không gán mặc định; cấp lẻ hoặc vai trò tùy biến (spec §5 cột "Bổ sung").
    public static IReadOnlySet<string> Supplementary { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "billing.deposit", "orders.cancel", "results.confirm", "billing.adjust", "billing.reopen",
        "billing.insurance-rate.override", "billing.emergency-exception.approve", "queue.display",
        "access-grants.request-emergency",
    };
}
```

> Lưu ý: `billing.deposit` vừa mặc định cho Thu ngân vừa là "bổ sung" cho Lễ tân — `Supplementary` chỉ là danh sách "đã quyết định", không cấm mã đó xuất hiện trong ma trận.

- [ ] **Step 5: Viết `RoleDefaultsPlan`**

```csharp
// PERS/Seed/RoleDefaultsPlan.cs
namespace QuanLyBenhVien.Persistence.Seed;

internal static class RoleDefaultsPlan
{
    public static IReadOnlyList<(string RoleCode, string PermissionCode)> PendingPairs(
        IReadOnlyDictionary<string, IReadOnlyList<string>> matrix,
        Func<string, bool> isActivated,
        IReadOnlySet<(string RoleCode, string PermissionCode)> alreadyApplied)
        => matrix
            .SelectMany(kv => kv.Value.Distinct(StringComparer.Ordinal).Select(code => (RoleCode: kv.Key, PermissionCode: code)))
            .Where(p => isActivated(p.PermissionCode) && !alreadyApplied.Contains(p))
            .OrderBy(p => p.RoleCode, StringComparer.Ordinal).ThenBy(p => p.PermissionCode, StringComparer.Ordinal)
            .ToList();
}
```

- [ ] **Step 6: Chạy unit test** `Persistence.Seed` → PASS.

- [ ] **Step 7: Entity, configuration, migration**

```csharp
// PERS/Seed/RolePermissionDefault.cs
namespace QuanLyBenhVien.Persistence.Seed;

/// Lịch sử: cặp (vai trò, quyền) mặc định đã được seeder áp. Có dòng ⇒ không bao giờ áp lại, kể cả khi admin đã bỏ quyền.
internal sealed class RolePermissionDefault
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
    public DateTimeOffset AppliedAt { get; private set; }

    private RolePermissionDefault() { }

    public RolePermissionDefault(Guid roleId, string permissionCode, DateTimeOffset appliedAt)
        => (RoleId, PermissionCode, AppliedAt) = (roleId, permissionCode, appliedAt);
}
```

```csharp
// PERS/Configurations/Identity/RolePermissionDefaultConfiguration.cs
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
```

Thêm vào `AppDbContext`: `internal DbSet<RolePermissionDefault> RolePermissionDefaults => Set<RolePermissionDefault>();`

Tạo migration (từ `BE`):
`dotnet ef migrations add RolePermissionDefaults --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API --output-dir Migrations`

Đọc file sinh ra: chỉ có `CreateTable RolePermissionDefaults` + FK. Thêm **backfill** vào `Up` (sau `CreateTable`) để các cặp đang có coi như đã áp (spec §8):

```csharp
migrationBuilder.Sql("""
    INSERT INTO "RolePermissionDefaults" ("RoleId", "PermissionCode", "AppliedAt")
    SELECT "RoleId", "PermissionCode", now() FROM "RolePermissions"
    ON CONFLICT DO NOTHING;
    """);
```

Kiểm tên cột thật của bảng `RolePermissions` trong snapshot trước khi viết SQL. Xem SQL: `dotnet ef migrations script <migration-trước> RolePermissionDefaults --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API`.

- [ ] **Step 8: Viết integration test seeder (đỏ trước)**

```csharp
// IT/Admin/RoleDefaultsSeederTests.cs — khung; dùng ApiFactory/TestData như RolesCommandTests
[Collection(IntegrationCollection.Name)]
public class RoleDefaultsSeederTests : IAsyncLifetime
{
    // InitializeAsync: _factory = await ApiFactory.CreateAsync(_containers);
    // Helper RunApplierAsync(): tạo scope, resolve RoleDefaultsApplier, gọi ApplyAsync.

    [Fact] // Startup đã chạy seeder: mọi cặp đã kích hoạt trong ma trận có trong RolePermissions và RolePermissionDefaults
    public async Task Startup_AppliesActivatedDefaults() { /* đọc DB qua TestData.QueryAsync, so với DefaultRolePermissions + Permissions.IsDefined */ }

    [Fact] // Chạy lại không thêm dòng nào
    public async Task Apply_IsIdempotent() { /* đếm 2 bảng trước/sau RunApplierAsync */ }

    [Fact] // Admin bỏ một quyền mặc định của vai trò doctor qua PUT /api/v1/roles/{id}/permissions → RunApplierAsync → quyền KHÔNG quay lại
    public async Task RemovedDefault_IsNotReapplied() { }

    [Fact] // Xóa dòng lịch sử của một cặp + bỏ quyền khỏi vai trò, giữ người dùng giữ vai trò đã làm ấm cache (GET /api/v1/auth/me);
           // RunApplierAsync → GET /me lần kế tiếp thấy quyền đó (invalidation chạy thật)
    public async Task NewlyAppliedDefault_IsVisibleOnNextRequest() { }

    [Fact] // 2 applier chạy song song (Task.WhenAll, mỗi cái scope riêng) sau khi xóa lịch sử một cặp → đúng 1 dòng lịch sử, không lỗi
    public async Task ConcurrentApply_DoesNotDuplicate() { }
}
```

Viết đầy đủ thân 5 test theo mô tả trong comment (helper đọc/ghi DB qua `TestData.QueryAsync`; dùng `_factory.LoginAsAdminAsync()` cho bước qua API). Chạy: `dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~RoleDefaultsSeeder"` → FAIL (chưa có applier).

- [ ] **Step 9: Viết `RoleDefaultsApplier` và nối vào seeder**

```csharp
// PERS/Seed/RoleDefaultsApplier.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Persistence.Seed;

internal sealed class RoleDefaultsApplier(AppDbContext db, ICacheInvalidator cacheInvalidator, TimeProvider time,
    ILogger<RoleDefaultsApplier> logger)
{
    /// Hằng khóa advisory riêng cho seeder quyền mặc định (không trùng khóa admin-safety).
    private const long AdvisoryLockKey = 0x48_4D_53_52_50_44; // "HMSRPD"

    public async Task ApplyAsync(CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

        var roles = await db.Roles.Include(r => r.GrantedPermissions).ToDictionaryAsync(r => r.Code, ct);
        var applied = (await db.RolePermissionDefaults
                .Join(db.Roles, d => d.RoleId, r => r.Id, (d, r) => new { r.Code, d.PermissionCode })
                .ToListAsync(ct))
            .Select(x => (x.Code, x.PermissionCode)).ToHashSet();

        var pending = RoleDefaultsPlan.PendingPairs(DefaultRolePermissions.ByRole, Permissions.IsDefined, applied)
            .Where(p => roles.ContainsKey(p.RoleCode)).ToList();
        if (pending.Count == 0) return;

        var now = time.GetUtcNow();
        foreach (var group in pending.GroupBy(p => p.RoleCode))
        {
            var role = roles[group.Key];
            var current = role.GrantedPermissions.Select(p => p.PermissionCode).ToList();
            if (role.SetPermissions(current.Union(group.Select(p => p.PermissionCode))))
            {
                var holders = await db.Set<UserRole>().Where(r => r.RoleId == role.Id).Select(r => r.UserId).ToListAsync(ct);
                foreach (var userId in holders) cacheInvalidator.InvalidatePermissions(userId);
            }
            foreach (var pair in group) db.RolePermissionDefaults.Add(new RolePermissionDefault(role.Id, pair.PermissionCode, now));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await cacheInvalidator.FlushAsync(ct);
        logger.LogInformation("Applied {Count} default role permissions", pending.Count);
    }
}
```

- Đăng ký `services.AddScoped<RoleDefaultsApplier>();` trong `PERS/DependencyInjection.cs`.
- `IdentitySeeder.SeedAsync`: sau `EnsureSystemRolesAsync` gọi `await _applier.ApplyAsync(ct);` (inject `RoleDefaultsApplier`). Giữ nguyên đoạn bảo vệ quyền lõi Admin hiện có.
- Nếu `RolePermission` thay đổi mà không bump `xmin` của Role: gọi `db.Entry(role).Property(r => r.Name).IsModified = true;` như `RoleRepository.MarkChanged` (đọc file đó và tái dùng cùng cách).

- [ ] **Step 10: Chạy test** — unit `Persistence.Seed` + integration `RoleDefaultsSeeder` + `RolesCommandTests` + `PermissionCatalogTests` → PASS.

- [ ] **Step 11: Commit**

```bash
git add benh_vien_be/src/QuanLyBenhVien.Persistence benh_vien_be/tests
git commit -m "feat(permissions): ma trận quyền mặc định và seeder áp một lần có lịch sử

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Domain và lưu trữ cơ cấu cơ sở/khoa/phòng

**Files:**
- Create: `DOM/Catalog/Facilities/Branch.cs`, `Department.cs`, `Room.cs`, `DepartmentKind.cs`, `IFacilityRepository.cs`
- Create: `PERS/Configurations/Catalog/BranchConfiguration.cs`, `DepartmentConfiguration.cs`, `RoomConfiguration.cs`
- Create: `PERS/Repositories/Catalog/FacilityRepository.cs`
- Modify: `PERS/AppDbContext.cs` (DbSet `Branches`, `Departments`, `Rooms`; thêm constraint vào `TranslatedUniqueConstraints`)
- Modify: `PERS/DependencyInjection.cs`
- Create: migration `<ts>_Facilities`
- Test: `UT/Domain/Catalog/FacilitiesTests.cs`, `IT/Persistence/FacilitiesPersistenceTests.cs`

**Interfaces:**
- Produces (Domain):

```csharp
public enum DepartmentKind { Clinical = 1, Laboratory = 2, Pharmacy = 3, Billing = 4, Administrative = 5 }

public sealed class Branch : AggregateRoot<Guid>, IAuditable
{
    public string Code { get; }  public string Name { get; }  public bool IsActive { get; }  public uint RowVersion { get; }
    public static Branch Create(string code, string name);
    public void Rename(string name);
    public void SetActive(bool isActive);
}
public sealed class Department : AggregateRoot<Guid>, IAuditable
{
    public Guid BranchId { get; } public string Code { get; } public string Name { get; } public DepartmentKind Kind { get; }
    public bool IsActive { get; } public uint RowVersion { get; }
    public static Department Create(Guid branchId, string code, string name, DepartmentKind kind);
    public void Rename(string name); public void SetActive(bool isActive);
}
public sealed class Room : AggregateRoot<Guid>, IAuditable
{
    public Guid DepartmentId { get; } public string Code { get; } public string Name { get; } public bool IsActive { get; } public uint RowVersion { get; }
    public static Room Create(Guid departmentId, string code, string name);
    public void Rename(string name); public void SetActive(bool isActive);
}
public interface IFacilityRepository
{
    Task<Branch?> GetBranchAsync(Guid id, CancellationToken ct = default);
    Task<Department?> GetDepartmentAsync(Guid id, CancellationToken ct = default);
    Task<Room?> GetRoomAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetDepartmentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task AddAsync(Branch branch, CancellationToken ct = default);
    Task AddAsync(Department department, CancellationToken ct = default);
    Task AddAsync(Room room, CancellationToken ct = default);
}
```

- Quy tắc Domain: `Code` khớp `^[A-Z0-9][A-Z0-9-]{0,29}$` (chữ hoa, số, gạch ngang, 1–30), `Name` không trắng, ≤ 200 (cắt khoảng trắng). `Code` và `Kind` không đổi sau khi tạo. Vi phạm → `ArgumentException` (validator ở Application chặn trước).
- Index unique: `IX_Branches_Code` trên `Code`; `IX_Departments_BranchId_Code`; `IX_Rooms_DepartmentId_Code`. FK `Department.BranchId → Branches` và `Room.DepartmentId → Departments` `Restrict`. `RowVersion` map `xmin`.

- [ ] **Step 1: Unit test Domain** — `FacilitiesTests`: tạo hợp lệ (Id v7 khác rỗng, Name trim), mã sai (`"ab"`, `"A B"`, 31 ký tự) → `ArgumentException`, tên trắng → `ArgumentException`, `Rename`/`SetActive` đổi giá trị, `Code` không có setter public.

```csharp
[Theory]
[InlineData("ab")] [InlineData("A B")] [InlineData("ABCDEFGHIJABCDEFGHIJABCDEFGHIJX")]
public void Branch_InvalidCode_Throws(string code) => Assert.Throws<ArgumentException>(() => Branch.Create(code, "Cơ sở 1"));

[Fact]
public void Department_Create_TrimsName_AndKeepsKind()
{
    var d = Department.Create(Guid.CreateVersion7(), "NOI", "  Khoa Nội  ", DepartmentKind.Clinical);
    Assert.Equal("Khoa Nội", d.Name);
    Assert.Equal(DepartmentKind.Clinical, d.Kind);
}
```

- [ ] **Step 2: Chạy** → FAIL. **Step 3:** viết 4 file Domain theo interface trên (mẫu theo `Role.cs`: private ctor, factory, `Guid.CreateVersion7()`, `Regex` compiled). **Step 4:** chạy → PASS.

- [ ] **Step 5: Configuration + repository + DbSet** theo mẫu `RoleConfiguration`/`RoleRepository` (`ToTable("Branches"|"Departments"|"Rooms")`, `HasMaxLength(30)` cho Code, `200` cho Name, `HasConversion<int>()` cho Kind, `IsRowVersion()`, `Ignore(DomainEvents)`). Thêm `"IX_Branches_Code"`, `"IX_Departments_BranchId_Code"`, `"IX_Rooms_DepartmentId_Code"` vào `TranslatedUniqueConstraints` và vào điều kiện `when` của `SaveChangesAsync`.

- [ ] **Step 6: Migration** `Facilities`; đọc file: 3 bảng, 3 unique index, 2 FK Restrict; nếu có `AddColumn "xmin"` thì loại khỏi Up/Down (cột hệ thống). Xem SQL bằng `dotnet ef migrations script`.

- [ ] **Step 7: Integration test persistence** — `FacilitiesPersistenceTests`: (a) lưu Branch→Department→Room rồi đọc lại; (b) trùng `(BranchId, Code)` → `UniqueConstraintViolationException` với `ConstraintName == "IX_Departments_BranchId_Code"`; (c) cùng Code ở hai cơ sở khác nhau → OK; (d) `RowVersion` tăng sau `Rename` + SaveChanges. Chạy → PASS.

- [ ] **Step 8: Commit** `feat(facilities): domain và lưu trữ cơ sở, khoa, phòng`.

---

### Task 3: API cơ cấu tổ chức (`facilities.read` / `facilities.manage`)

**Files:**
- Modify: `DOM/Identity/Permissions.cs` (class lồng `Facilities` + danh sách `Organization`, nối vào `All`)
- Create: `APP/Features/Facilities/Common/{FacilitiesErrors.cs, FacilityTreeDto.cs, IFacilitiesReadService.cs}`
- Create: `APP/Features/Facilities/GetFacilityTree/{GetFacilityTreeQuery.cs, GetFacilityTreeQueryHandler.cs}`
- Create: `APP/Features/Facilities/CreateBranch/{Command, Handler, Validator}.cs`, `CreateDepartment/…`, `CreateRoom/…`, `UpdateBranch/…`, `UpdateDepartment/…`, `UpdateRoom/…`
- Modify: `APP/Common/Auditing/AuditActions.cs`
- Create: `PERS/ReadServices/Catalog/FacilitiesReadService.cs`
- Create: `PRES/Endpoints/V1/Facilities/{FacilitiesEndpoints.cs, CreateBranchRequest.cs, CreateDepartmentRequest.cs, CreateRoomRequest.cs, UpdateFacilityRequest.cs}`
- Modify: `GW/appsettings.json` (route `facilities-route`)
- Test: `IT/Admin/FacilitiesAdminTests.cs`, `UT/Application/Features/Facilities/FacilityValidatorsTests.cs`

**Interfaces:**
- Consumes: Task 2 Domain/`IFacilityRepository`; Task 1 seeder (mã mới tự được gán cho vai trò theo ma trận khi khởi động).
- Produces:

```csharp
// Permissions.cs
public static class Facilities { public const string Read = "facilities.read"; public const string Manage = "facilities.manage"; }
public static class StaffProfiles { public const string Read = "staff-profiles.read"; public const string Manage = "staff-profiles.manage"; } // khai ở Task 5
public static IReadOnlyList<PermissionDefinition> Organization { get; } =
[
    new(Facilities.Read, "Cơ cấu tổ chức", "Xem cơ sở, khoa, phòng"),
    new(Facilities.Manage, "Cơ cấu tổ chức", "Tạo, đổi tên, ngừng dùng cơ sở, khoa, phòng"),
];
public static IReadOnlyList<PermissionDefinition> All { get; } = [.. IdentityAccess, .. Organization];

// DTO
public sealed record RoomDto(Guid Id, string Code, string Name, bool IsActive, uint RowVersion);
public sealed record DepartmentDto(Guid Id, string Code, string Name, string Kind, bool IsActive, uint RowVersion, IReadOnlyList<RoomDto> Rooms);
public sealed record BranchDto(Guid Id, string Code, string Name, bool IsActive, uint RowVersion, IReadOnlyList<DepartmentDto> Departments);
public interface IFacilitiesReadService { Task<IReadOnlyList<BranchDto>> GetTreeAsync(CancellationToken ct); }

// Commands (đều trả Result<BranchDto|DepartmentDto|RoomDto> — DTO không kèm con cho Create/Update)
public sealed record CreateBranchCommand(string Code, string Name) : ICommand<Result<BranchDto>>;
public sealed record CreateDepartmentCommand(Guid BranchId, string Code, string Name, string Kind) : ICommand<Result<DepartmentDto>>;
public sealed record CreateRoomCommand(Guid DepartmentId, string Code, string Name) : ICommand<Result<RoomDto>>;
public sealed record UpdateBranchCommand(Guid Id, string Name, bool IsActive, uint ExpectedVersion) : ICommand<Result<BranchDto>>;
public sealed record UpdateDepartmentCommand(Guid Id, string Name, bool IsActive, uint ExpectedVersion) : ICommand<Result<DepartmentDto>>;
public sealed record UpdateRoomCommand(Guid Id, string Name, bool IsActive, uint ExpectedVersion) : ICommand<Result<RoomDto>>;
```

- Route (group `/api/v1/facilities`, tag `Facilities`):

| Method | Route | Quyền | Name | Kết quả |
|---|---|---|---|---|
| GET | `/` | `facilities.read` | `GetFacilityTreeV1` | 200 `BranchDto[]` sắp theo Code; con sắp theo Code |
| POST | `/branches` | `facilities.manage` + CSRF | `CreateBranchV1` | 201 |
| POST | `/departments` | như trên | `CreateDepartmentV1` | 201 |
| POST | `/rooms` | như trên | `CreateRoomV1` | 201 |
| PUT | `/branches/{id}` | như trên + If-Match | `UpdateBranchV1` | 200 |
| PUT | `/departments/{id}` | như trên + If-Match | `UpdateDepartmentV1` | 200 |
| PUT | `/rooms/{id}` | như trên + If-Match | `UpdateRoomV1` | 200 |

- Lỗi (`FacilitiesErrors`): `facility_not_found` (NotFound), `facility_code_taken` (Conflict), `facility_version_conflict` (Precondition), `parent_facility_inactive` (Conflict — tạo con dưới cha đã ngừng dùng), `facility_has_active_children` (Conflict — ngừng dùng cha khi còn con đang dùng).
- `Kind` nhận `"clinical" | "laboratory" | "pharmacy" | "billing" | "administrative"` (validator); DTO trả cùng dạng chữ thường.
- `AuditActions`: `FacilityCreate = "facilities.create"`, `FacilityUpdate = "facilities.update"`; ghi `resourceType` `"Branch"|"Department"|"Room"`.
- Query/command trong `Features/Facilities` implement `IUnscopedRequest` và được thêm vào `ApprovedUnscopedRequests` (Task 7) với lý do "Danh mục cơ cấu tổ chức, không chứa PHI; quyền lớp 1 đủ". Task 7 phải xong trước Task 3 — xem thứ tự thực thi ở đầu plan.

- [ ] **Step 1: Integration test (đỏ)** — `FacilitiesAdminTests`:
  1. admin tạo cơ sở → 201, `GET /api/v1/facilities` có cơ sở; tạo khoa `kind=clinical` + phòng → cây đúng.
  2. trùng mã khoa trong cùng cơ sở → 409 `facility_code_taken`; mã sai định dạng → 400.
  3. `PUT /branches/{id}` thiếu If-Match → 400 `invalid_if_match`; version cũ → 412; đúng → 200 version mới.
  4. ngừng dùng khoa còn phòng đang dùng → 409 `facility_has_active_children`; tạo phòng dưới khoa đã ngừng → 409 `parent_facility_inactive`.
  5. người có vai trò `doctor` (ma trận cho `facilities.read`) → GET 200, POST 403; người không vai trò → GET 403; ẩn danh → 401.
  6. mỗi mutation có `AuditRecord` với action tương ứng.
- [ ] **Step 2: Chạy** → FAIL (404 route). **Step 3:** Unit test validator (`CreateDepartmentCommandValidator`: kind lạ → lỗi; code sai → lỗi) → FAIL.
- [ ] **Step 4: Implement** theo mẫu `Features/Roles` (handler `internal sealed`, validator `internal sealed`, `Result` + `FacilitiesErrors`, bắt `UniqueConstraintViolationException` đúng tên constraint → `facility_code_taken`, kiểm `ExpectedVersion` với `RowVersion` → `facility_version_conflict`). Endpoint theo mẫu `RolesEndpoints` (tách `TryReadIfMatch`/`InvalidIfMatch` thành helper dùng chung `PRES/Http/IfMatch.cs` nếu chưa có — đọc `RolesEndpoints`/`UsersEndpoints` trước; nếu cả hai đang tự định nghĩa thì tạo helper và **chỉ** dùng cho Facilities/StaffProfiles, không sửa Roles/Users). Read service: `AsNoTracking`, một truy vấn mỗi bảng rồi ghép cây trong bộ nhớ (số lượng nhỏ, không phân trang).
- [ ] **Step 5:** Thêm route Gateway:

```json
"facilities-route": {
  "ClusterId": "api-cluster",
  "Match": { "Path": "/api/v1/facilities/{**catch-all}" }
}
```

(chép đúng `ClusterId` và các trường khác từ `roles-route` hiện có).
- [ ] **Step 6: Chạy** integration `FacilitiesAdmin`, `RoleDefaultsSeeder`, `PermissionCatalog` (danh mục quyền giờ có 10 mã) → PASS; sửa assertion số lượng mã trong test cũ nếu có kiểm cứng 8 (đó là thay đổi hành vi cố ý — ghi lý do trong report).
- [ ] **Step 7: Commit** `feat(facilities): API cây cơ cấu và tạo/cập nhật cơ sở, khoa, phòng`.

---

### Task 4: Domain và lưu trữ hồ sơ nhân sự, phạm vi làm việc

**Files:**
- Create: `DOM/Identity/Staff/StaffProfile.cs`, `StaffWorkScope.cs`, `IStaffProfileRepository.cs`
- Create: `PERS/Configurations/Identity/StaffProfileConfiguration.cs`, `StaffWorkScopeConfiguration.cs`
- Create: `PERS/Repositories/Identity/StaffProfileRepository.cs`
- Modify: `PERS/AppDbContext.cs`, `PERS/DependencyInjection.cs`
- Create: migration `<ts>_StaffProfiles`
- Test: `UT/Domain/Identity/StaffProfileTests.cs`, `IT/Persistence/StaffProfilePersistenceTests.cs`

**Interfaces:**

```csharp
public sealed class StaffWorkScope
{
    public Guid StaffProfileId { get; }  public Guid DepartmentId { get; }  public Guid BranchId { get; }
}
public sealed class StaffProfile : AggregateRoot<Guid>, IAuditable
{
    public Guid UserId { get; }  public string StaffCode { get; }  public bool IsActive { get; }  public uint RowVersion { get; }
    public IReadOnlyCollection<StaffWorkScope> WorkScopes { get; }
    public static StaffProfile Create(Guid userId, string staffCode);
    public void ChangeStaffCode(string staffCode);
    public void SetActive(bool isActive);
    /// departments: (DepartmentId, BranchId) đã được Application kiểm tồn tại + đang dùng. Trả true nếu tập thay đổi.
    public bool SetWorkScopes(IEnumerable<(Guid DepartmentId, Guid BranchId)> departments);
}
public interface IStaffProfileRepository
{
    /// Kèm WorkScopes, tracking.
    Task<StaffProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(StaffProfile profile, CancellationToken ct = default);
    /// Buộc UPDATE StaffProfile (bump xmin) khi chỉ bảng con StaffWorkScopes đổi.
    void MarkChanged(StaffProfile profile);
}
```

- `StaffCode`: `^[A-Z0-9][A-Z0-9-]{1,29}$` (2–30); unique `IX_StaffProfiles_StaffCode`. `UserId` unique `IX_StaffProfiles_UserId`, FK → `Users` Restrict. Tài khoản thiết bị dùng tiền tố `DEV-` (quy ước, không kiểm cứng).
- `StaffWorkScopes`: PK `(StaffProfileId, DepartmentId)`, FK `StaffProfileId` cascade, FK `DepartmentId → Departments` Restrict, FK `BranchId → Branches` Restrict; index `(BranchId)`.
- Thêm `"IX_StaffProfiles_StaffCode"`, `"IX_StaffProfiles_UserId"` vào `TranslatedUniqueConstraints`.

- [ ] **Step 1: Unit test** — tạo hợp lệ; mã sai → `ArgumentException`; `SetWorkScopes` thay nguyên tập, trùng `DepartmentId` bị gộp, trả `false` khi tập không đổi, `true` khi đổi.
- [ ] **Step 2:** Chạy → FAIL. **Step 3:** Implement (mẫu `Role.SetPermissions`). **Step 4:** PASS.
- [ ] **Step 5:** Configuration, repository (`MarkChanged` như `RoleRepository.MarkChanged`), DbSet, migration `StaffProfiles` (loại `xmin` khỏi Up/Down nếu EF sinh), xem SQL.
- [ ] **Step 6: Integration test persistence** — lưu profile + 2 scope; trùng `StaffCode` → `UniqueConstraintViolationException("IX_StaffProfiles_StaffCode")`; hai profile cùng `UserId` → `"IX_StaffProfiles_UserId"`; chỉ đổi scope + `MarkChanged` → `RowVersion` tăng. PASS.
- [ ] **Step 7: Commit** `feat(staff): domain và lưu trữ hồ sơ nhân sự, phạm vi làm việc`.

---

### Task 5: API hồ sơ nhân sự (`staff-profiles.read` / `staff-profiles.manage`)

**Files:**
- Modify: `DOM/Identity/Permissions.cs` (class `StaffProfiles` + thêm 2 định nghĩa vào `Organization`)
- Create: `APP/Features/StaffProfiles/Common/{StaffProfilesErrors.cs, StaffProfileDto.cs, IStaffProfilesReadService.cs}`
- Create: `APP/Features/StaffProfiles/GetStaffProfile/{Query, Handler}.cs`, `UpsertStaffProfile/{Command, Handler, Validator}.cs`, `SetStaffWorkScopes/{Command, Handler, Validator}.cs`
- Modify: `APP/Common/Auditing/AuditActions.cs`
- Create: `PERS/ReadServices/Identity/StaffProfilesReadService.cs`
- Create: `PRES/Endpoints/V1/StaffProfiles/{StaffProfilesEndpoints.cs, UpsertStaffProfileRequest.cs, SetStaffWorkScopesRequest.cs}`
- Test: `IT/Admin/StaffProfilesAdminTests.cs`

**Interfaces:**
- Consumes: Task 4 (`IStaffProfileRepository`, `StaffProfile`), Task 2 (`IFacilityRepository.GetDepartmentsAsync`).
- Produces:

```csharp
public static class StaffProfiles { public const string Read = "staff-profiles.read"; public const string Manage = "staff-profiles.manage"; }
// thêm vào Organization:
new(StaffProfiles.Read, "Nhân sự", "Xem hồ sơ nhân sự và phạm vi làm việc"),
new(StaffProfiles.Manage, "Nhân sự", "Tạo, sửa hồ sơ nhân sự và gán cơ sở/khoa làm việc"),

public sealed record StaffWorkScopeDto(Guid BranchId, string BranchName, Guid DepartmentId, string DepartmentName, string DepartmentKind);
public sealed record StaffProfileDto(Guid Id, Guid UserId, string StaffCode, bool IsActive, uint RowVersion, IReadOnlyList<StaffWorkScopeDto> WorkScopes);
public sealed record GetStaffProfileQuery(Guid UserId) : IQuery<Result<StaffProfileDto>>;
/// ExpectedVersion null = tạo mới (chưa có hồ sơ); có giá trị = cập nhật hồ sơ có sẵn.
public sealed record UpsertStaffProfileCommand(Guid UserId, string StaffCode, bool IsActive, uint? ExpectedVersion) : ICommand<Result<StaffProfileDto>>;
public sealed record SetStaffWorkScopesCommand(Guid UserId, IReadOnlyList<Guid> DepartmentIds, uint ExpectedVersion) : ICommand<Result<StaffProfileDto>>;
```

- Route (trong group `/api/v1/users/{userId:guid}/staff-profile`, tag `StaffProfiles`; Gateway `users-route` đã phủ):

| Method | Route | Quyền | Name | Ghi chú |
|---|---|---|---|---|
| GET | `` | `staff-profiles.read` | `GetStaffProfileV1` | 404 `staff_profile_not_found` nếu chưa có |
| PUT | `` | `staff-profiles.manage` + CSRF | `UpsertStaffProfileV1` | Không có If-Match ⇒ tạo (409 `staff_profile_exists` nếu đã có); có If-Match ⇒ cập nhật (404 nếu chưa có, 412 nếu cũ). 201 khi tạo, 200 khi cập nhật |
| PUT | `/work-scopes` | như trên + If-Match bắt buộc | `SetStaffWorkScopesV1` | `{departmentIds[]}`; khoa không tồn tại/ngừng dùng → 400 `invalid_departments` (liệt kê id trong `errors`) |

- Lỗi: `staff_profile_not_found`, `staff_profile_exists`, `staff_code_taken`, `staff_profile_version_conflict`, `invalid_departments`, `user_not_found` (user không tồn tại).
- `AuditActions`: `StaffProfileCreate = "staff_profiles.create"`, `StaffProfileUpdate = "staff_profiles.update"`, `StaffWorkScopesSet = "staff_profiles.set_work_scopes"` (metadata: danh sách `DepartmentId` mới — không có PHI).
- Thay đổi phạm vi làm việc **không** cần invalidation cache quyền (lớp 2 đọc DB mỗi request — spec §3.3).
- Request implement `IUnscopedRequest` (lý do: dữ liệu quản trị nhân sự, quyền lớp 1 đủ).

- [ ] **Step 1: Integration test (đỏ)** — `StaffProfilesAdminTests`:
  1. user mới → GET 404 `staff_profile_not_found`; PUT không If-Match → 201; PUT lần 2 không If-Match → 409 `staff_profile_exists`.
  2. PUT có If-Match đổi `staffCode`/`isActive` → 200 version mới; version cũ → 412; trùng `staffCode` với người khác → 409 `staff_code_taken`; mã sai → 400.
  3. `PUT /work-scopes` với 2 khoa ở cùng cơ sở → 200, `workScopes` có `branchId` đúng; thêm id khoa không tồn tại hoặc đã ngừng → 400 `invalid_departments`, không đổi dữ liệu; thiếu If-Match → 400.
  4. vai trò `clinical-manager` (ma trận có `staff-profiles.read`) → GET 200, PUT 403; `doctor` → GET 403.
  5. audit đủ 3 action.
- [ ] **Step 2:** FAIL. **Step 3:** Implement theo mẫu Task 3. Handler `SetStaffWorkScopes`: tải profile (404), so version (412), `facilities.GetDepartmentsAsync(ids)` → mọi id phải tồn tại và `IsActive` (thiếu → `invalid_departments` với `FieldErrors["departmentIds"]` liệt kê id), `profile.SetWorkScopes(...)`; nếu đổi thì `MarkChanged`, audit, SaveChanges; trả DTO đọc lại qua read service.
- [ ] **Step 4:** Chạy `StaffProfilesAdmin` + `FacilitiesAdmin` + `RoleDefaultsSeeder` + `PermissionCatalog` → PASS.
- [ ] **Step 5: Commit** `feat(staff): API hồ sơ nhân sự và gán cơ sở/khoa làm việc`.

---

### Task 6: Khung lớp 2 — `IAccessContext`, `IResourceAuthorizer`, `IDeniedAccessRecorder`

**Files:**
- Create: `APP/Common/Authorization/{AccessScope.cs, IAccessContext.cs, ResourceRef.cs, RelationKind.cs, AccessDecision.cs, IResourceAuthorizer.cs, IResourceScopePolicy.cs, IDeniedAccessRecorder.cs, ResourceAccessErrors.cs}`
- Create: `PERS/Authorization/{AccessContext.cs, ResourceAuthorizer.cs, DeniedAccessRecorder.cs}`
- Modify: `PERS/DependencyInjection.cs` (đăng ký + `AddDbContextFactory`), `APP/Common/Auditing/AuditActions.cs`
- Test: `UT/Persistence/Authorization/ResourceAuthorizerTests.cs`, `IT/Authorization/AccessContextTests.cs`, `IT/Authorization/DeniedAccessRecorderTests.cs`

**Interfaces (Produces — đúng như spec §7.1, §7.5):**

```csharp
namespace QuanLyBenhVien.Application.Common.Authorization;

public sealed record AccessScope(Guid UserId, Guid? StaffProfileId, IReadOnlySet<Guid> BranchIds, IReadOnlySet<Guid> DepartmentIds)
{
    /// Không có hồ sơ nhân sự đang dùng hoặc không có phạm vi ⇒ không qua được bước 3 với mọi tài nguyên CS/QH.
    public bool HasWorkScope => StaffProfileId is not null && BranchIds.Count > 0;
}

public interface IAccessContext { Task<AccessScope> GetAsync(CancellationToken ct); }

public sealed record ResourceRef(string ResourceType, Guid Id);

public enum RelationKind { SessionStaff, DepartmentScope, CareTeam, DepartmentRouting, Grant }

public abstract record AccessDecision
{
    public sealed record Allowed(RelationKind Relation, string? DutyRole) : AccessDecision;
    public sealed record Denied(string Reason) : AccessDecision;
}

public interface IResourceAuthorizer
{
    Task<AccessDecision> AuthorizeAsync(string permissionCode, ResourceRef resource, CancellationToken ct);
}

/// Module đăng ký một policy cho mỗi cặp (PermissionCode, ResourceType). Implement ở Persistence của module.
public interface IResourceScopePolicy
{
    string PermissionCode { get; }
    string ResourceType { get; }
    Task<AccessDecision> EvaluateAsync(AccessScope scope, Guid resourceId, DateTimeOffset now, CancellationToken ct);
}

public interface IDeniedAccessRecorder
{
    Task RecordAsync(string action, ResourceRef resource, string reason, CancellationToken ct);
}

public static class ResourceAccessErrors
{
    public static readonly Error NotFound = new("resource_not_found", "Không tìm thấy hoặc bạn không có quyền truy cập.", ErrorType.NotFound);
}
```

`AuditActions.ResourceAccessDenied = "auth.resource.denied"`.

**Hành vi:**
- `AccessContext` (scoped): lần đầu `GetAsync` đọc `StaffProfiles` (theo `ICurrentUser.UserId`, chỉ hồ sơ `IsActive`) + `StaffWorkScopes` join `Departments`/`Branches` **đang dùng**; lưu kết quả trong field cho các lần gọi sau cùng request. Người dùng chưa đăng nhập → `InvalidOperationException` (lỗi lập trình: route lớp 2 luôn yêu cầu đăng nhập).
- `ResourceAuthorizer`: nhận `IEnumerable<IResourceScopePolicy>` từ DI, dựng dictionary theo `(PermissionCode, ResourceType)` (trùng cặp → `InvalidOperationException` lúc khởi tạo). `AuthorizeAsync`: không có policy → log `Error` "No resource policy for {Permission} {ResourceType}" và trả `Denied("no_policy")`; `scope.HasWorkScope == false` → `Denied("no_work_scope")`; còn lại gọi policy với `now = TimeProvider.GetUtcNow()`. **Không** kiểm lớp 1 ở đây (đã kiểm trên route); policy grant tự kiểm lại lớp 1 khi cần (mốc 4).
- `DeniedAccessRecorder`: inject `IDbContextFactory<AppDbContext>`, `ICurrentUser`, `IRequestContext`, `TimeProvider`, `ILogger`. Tạo context mới, `AuditRecords.Add(AuditRecord.Create(actor, action, AuditResult.Denied?, reason, resource.ResourceType, resource.Id.ToString(), correlation, ip, ua, null, now))` rồi `SaveChangesAsync` của context đó. Bắt mọi exception → log `Error` kèm correlation id, **không ném lại**. (Kiểm enum `AuditResult` hiện có giá trị từ chối nào — dùng giá trị mà `ProblemAuthorizationResultHandler` đang dùng cho 403.)
- DI: giữ `AddDbContext<AppDbContext>(configure)` hiện tại và thêm `services.AddDbContextFactory<AppDbContext>(configure, ServiceLifetime.Scoped);` dùng **cùng** delegate cấu hình (tách delegate ra biến cục bộ). Đăng ký `IAccessContext → AccessContext` (scoped), `IResourceAuthorizer → ResourceAuthorizer` (scoped), `IDeniedAccessRecorder → DeniedAccessRecorder` (scoped). Chưa đăng ký policy nào trong plan này.

- [ ] **Step 1: Unit test `ResourceAuthorizer`** (fake `IAccessContext`, fake policy, `FakeTimeProvider` nếu project đã dùng — nếu không, một `TimeProvider` con tự viết):
  - không policy → `Denied("no_policy")`;
  - có policy nhưng scope không có phạm vi → `Denied("no_work_scope")`, policy **không** được gọi;
  - có policy + phạm vi → trả đúng quyết định của policy và truyền `now` từ `TimeProvider`;
  - hai policy cùng cặp → khởi tạo ném `InvalidOperationException`.
- [ ] **Step 2:** FAIL → implement interface Application + `ResourceAuthorizer` → PASS.
- [ ] **Step 3: Integration test `AccessContextTests`** — tạo user, hồ sơ nhân sự, 2 khoa thuộc 2 cơ sở (1 khoa đã ngừng dùng) qua `TestData`/DB; resolve `IAccessContext` trong scope có `ICurrentUser` của user đó (xem cách các IT hiện có giả `ICurrentUser`; nếu không có, gọi qua một scope và override bằng `TestServices`) → `BranchIds`/`DepartmentIds` chỉ gồm khoa đang dùng; hồ sơ `IsActive=false` → `StaffProfileId == null`, `HasWorkScope == false`; gọi `GetAsync` hai lần chỉ truy vấn DB một lần (đếm bằng interceptor đếm lệnh hoặc kiểm cùng instance trả về).
- [ ] **Step 4: Integration test `DeniedAccessRecorderTests`** (spec §7.5, §9.3):

```csharp
[Fact]
public async Task Denied_AuditSurvivesRollback_BusinessChangeDoesNot()
{
    var roleId = /* tạo role "rec-test-<guid>" qua TestData/DB */;
    await using (var scope = _factory.Services.CreateAsyncScope())
    {
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var recorder = scope.ServiceProvider.GetRequiredService<IDeniedAccessRecorder>();

        await using (var tx = await uow.BeginTransactionAsync())
        {
            var role = await roles.GetForUpdateAsync(roleId);   // giữ khóa hàng như command thật
            role!.Rename("Đổi trong transaction sẽ rollback");
            await uow.SaveChangesAsync();
        }                                                    // dispose không commit ⇒ rollback

        await recorder.RecordAsync(AuditActions.ResourceAccessDenied, new ResourceRef("Role", roleId), "test_denied", default);
    }

    var name = await TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync());
    Assert.NotEqual("Đổi trong transaction sẽ rollback", name);
    Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
        a.Action == AuditActions.ResourceAccessDenied && a.ResourceId == roleId.ToString() && a.Reason == "test_denied")));
}

[Fact]
public async Task RecorderFailure_DoesNotThrow() { /* dùng ResourceRef có ResourceType dài vượt cột → SaveChanges lỗi; RecordAsync không ném */ }
```

Kiểm tên thuộc tính thật của `AuditRecord` (`Action`, `ResourceId`, `Reason`…) trong `DOM/Common/Auditing/AuditRecord.cs` trước khi viết assertion.
- [ ] **Step 5:** FAIL → implement `AccessContext`, `DeniedAccessRecorder`, DI → PASS. Chạy thêm toàn bộ IT `Authorization` và `Auth` để chắc `AddDbContextFactory` không phá DbContext scoped hiện có.
- [ ] **Step 6: Commit** `feat(authz): khung lớp 2 — AccessContext, ResourceAuthorizer, audit từ chối sống qua rollback`.

---

### Task 7: Marker `IScopedRequest`/`IUnscopedRequest` và architecture test

**Files:**
- Create: `APP/Common/Authorization/IScopedRequest.cs`, `IUnscopedRequest.cs`
- Create: `UT/Architecture/ScopedRequestRules.cs`, `UT/Architecture/ApprovedUnscopedRequests.cs`, `UT/Architecture/ScopedRequestRulesTests.cs`

**Interfaces:**

```csharp
namespace QuanLyBenhVien.Application.Common.Authorization;
/// Request chạm tài nguyên có lớp 2 (spec §7.4): handler PHẢI lọc/kiểm qua IAccessContext/IResourceAuthorizer.
public interface IScopedRequest;
/// Request chủ ý không lọc phạm vi. PHẢI có mục trong ApprovedUnscopedRequests (test kiến trúc) kèm lý do.
public interface IUnscopedRequest;
```

```csharp
// UT/Architecture/ScopedRequestRules.cs
namespace QuanLyBenhVien.UnitTests.Architecture;

internal static class ScopedRequestRules
{
    /// Feature cần marker: các module có lớp 2 (spec §7.4) + module nền của plan này.
    public static readonly string[] GovernedFeatures =
        ["ReceptionQueue", "Clinical", "Inpatient", "Billing", "Documents", "Pharmacy", "Facilities", "StaffProfiles"];

    public static IReadOnlyList<string> Violations(IEnumerable<Type> types, IReadOnlyDictionary<Type, string> approved)
    {
        var violations = new List<string>();
        foreach (var t in types.Where(IsRequest))
        {
            var scoped = typeof(IScopedRequest).IsAssignableFrom(t);
            var unscoped = typeof(IUnscopedRequest).IsAssignableFrom(t);
            if (scoped && unscoped) violations.Add($"{t.FullName}: implement cả hai marker");
            else if (!scoped && !unscoped && IsGoverned(t)) violations.Add($"{t.FullName}: thiếu marker");
            else if (unscoped && !approved.ContainsKey(t)) violations.Add($"{t.FullName}: IUnscopedRequest chưa được duyệt");
        }
        foreach (var t in approved.Keys.Where(t => !typeof(IUnscopedRequest).IsAssignableFrom(t)))
            violations.Add($"{t.FullName}: có trong ApprovedUnscopedRequests nhưng không implement IUnscopedRequest");
        return violations;
    }

    private static bool IsRequest(Type t) => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(i =>
        i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>)));

    private static bool IsGoverned(Type t) => GovernedFeatures.Any(f => t.Namespace?.Contains($".Features.{f}.", StringComparison.Ordinal) == true);
}
```

```csharp
// UT/Architecture/ApprovedUnscopedRequests.cs — thêm mục = thay đổi phải qua review; mỗi mục có lý do.
internal static class ApprovedUnscopedRequests
{
    public static IReadOnlyDictionary<Type, string> All { get; } = new Dictionary<Type, string>
    {
        // Task 3 và Task 5 thêm các mục của Facilities/StaffProfiles tại đây.
    };
}
```

- [ ] **Step 1: Unit test luật** với type giả lồng trong test (namespace chứa `.Features.Clinical.`): thiếu marker → vi phạm; hai marker → vi phạm; unscoped chưa duyệt → vi phạm; approved trỏ type không implement → vi phạm; type ngoài feature bị quản lý và không marker → không vi phạm. (Đặt type giả trong namespace `QuanLyBenhVien.UnitTests.Architecture.Fakes.Features.Clinical.X` để `IsGoverned` khớp.)
- [ ] **Step 2: Test thật** trên assembly Application:

```csharp
[Fact]
public void ApplicationRequests_FollowScopedRequestRules()
{
    var types = typeof(IScopedRequest).Assembly.GetTypes();
    Assert.Empty(ScopedRequestRules.Violations(types, ApprovedUnscopedRequests.All));
}
```

- [ ] **Step 3:** FAIL (chưa có marker) → tạo 2 interface → PASS (chưa có request nào trong feature bị quản lý nếu Task 3/5 chưa chạy). Khi Task 3 và Task 5 thêm request, chúng **phải** thêm mục vào `ApprovedUnscopedRequests` với lý do ("Danh mục cơ cấu tổ chức, không chứa PHI; quyền lớp 1 đủ" / "Quản trị hồ sơ nhân sự; quyền lớp 1 đủ").
- [ ] **Step 4: Commit** `test(arch): marker IScopedRequest/IUnscopedRequest và danh sách ngoại lệ được duyệt`.

---

### Task 8: FE — màn Cơ cấu tổ chức (`/admin/facilities`)

**Files:**
- Modify: `FE/feature/Auth/permissionCodes.js` (thêm `FACILITIES_READ`, `FACILITIES_MANAGE`, `STAFF_PROFILES_READ`, `STAFF_PROFILES_MANAGE`)
- Modify: `FE/feature/Workspace/routeAccess.js` (route `/admin/facilities`, `availability: 'admin.facilities'`, label `'Cơ cấu tổ chức'`), `FE/feature/Workspace/availability.js` (`'admin.facilities': !isMockApiEnabled()`), `FE/feature/Workspace/workspaces.js` (mô tả khu vực quản trị thêm "cơ cấu tổ chức, nhân sự")
- Modify: `FE/index.js`, `FE/reducer.js`, `FE/testUtils/appHarness.js` (đăng ký route/reducer như Roles)
- Create: `FE/feature/Facilities/{index.js, Container.js, api/facilitiesClient.js, redux/action.js, redux/reducer.js, component/FacilityTree.js, component/FacilityDialog.js, __tests__/facilitiesClient.test.js, __tests__/Container.test.js, __tests__/reducer.test.js}`

**Interfaces:**
- Consumes: API Task 3.
- Produces:

```js
// api/facilitiesClient.js
export const getFacilityTree = () => http.get('v1/facilities').then(r => r.data);
export const createBranch = payload => http.post('v1/facilities/branches', payload).then(r => r.data);
export const createDepartment = payload => http.post('v1/facilities/departments', payload).then(r => r.data);
export const createRoom = payload => http.post('v1/facilities/rooms', payload).then(r => r.data);
export const updateFacility = (type, id, { name, isActive }, rowVersion) =>
  http.put(`v1/facilities/${type}/${id}`, { name, isActive }, { headers: { 'If-Match': String(rowVersion) } }).then(r => r.data);
// type ∈ 'branches' | 'departments' | 'rooms'
```

- Action type: `'HMS/FACILITIES/LOAD_REQUEST' | '…/LOAD_SUCCESS' | '…/LOAD_FAILURE'`; sau mỗi mutation thành công nạp lại cây (đơn giản, dữ liệu nhỏ).

**Hành vi UI:**
- Cây: cơ sở → khoa (kèm nhãn loại: Lâm sàng/Xét nghiệm/Dược/Viện phí/Hành chính) → phòng; mục ngừng dùng hiện badge "Ngừng dùng".
- Có `facilities.manage`: nút "Thêm cơ sở", "Thêm khoa" (trên cơ sở), "Thêm phòng" (trên khoa), "Sửa" (tên + đang dùng). Không có: chỉ đọc, ẩn nút.
- Dialog tạo: mã (gợi ý chữ hoa), tên, loại (chỉ khoa). Lỗi 400/409 hiện theo `code` (`facility_code_taken` → "Mã đã tồn tại trong cùng cấp", `parent_facility_inactive`, `facility_has_active_children`). 412 → "Dữ liệu đã thay đổi, đã nạp lại" + nạp lại cây, giữ dialog mở với giá trị người dùng nhập.
- Lỗi tải → thông báo + nút "Thử lại" (như Roles).

- [ ] **Step 1: Test client** (`facilitiesClient.test.js`, mock `service/http` như `rolesClient.test.js`): đúng URL/method/body; `updateFacility` gửi `If-Match`.
- [ ] **Step 2: Test reducer + Container**: tải thành công hiện cây; lỗi tải + "Thử lại"; không có `facilities.manage` thì không có nút thêm; tạo khoa thành công → gọi `createDepartment` với `branchId` đúng rồi nạp lại; 409 hiện thông báo theo mã; 412 nạp lại và giữ dialog.
- [ ] **Step 3:** Chạy `Facilities` → FAIL. **Step 4:** Implement. **Step 5:** Chạy `Facilities`, `Workspace`, `PermissionRoute` (sửa test registry nếu đang kiểm cứng số route — ghi lý do) → PASS; `npx eslint src/feature/Facilities src/feature/Workspace` sạch.
- [ ] **Step 6: Commit** `feat(fe-facilities): màn cơ cấu tổ chức cơ sở, khoa, phòng`.

---

### Task 9: FE — mục "Hồ sơ nhân sự" trong panel tài khoản

**Files:**
- Create: `FE/feature/Admin/api/staffProfileClient.js`, `FE/feature/Admin/component/StaffProfileSection.js`, `FE/feature/Admin/__tests__/staffProfileClient.test.js`, `FE/feature/Admin/__tests__/StaffProfileSection.test.js`
- Modify: `FE/feature/Admin/component/UserDetailPanel.js` (render `StaffProfileSection` khi có `staff-profiles.read`)

**Interfaces:**

```js
// api/staffProfileClient.js
export const getStaffProfile = userId => http.get(`v1/users/${userId}/staff-profile`).then(r => r.data);
export const createStaffProfile = (userId, { staffCode, isActive }) =>
  http.put(`v1/users/${userId}/staff-profile`, { staffCode, isActive }).then(r => r.data);
export const updateStaffProfile = (userId, { staffCode, isActive }, rowVersion) =>
  http.put(`v1/users/${userId}/staff-profile`, { staffCode, isActive }, { headers: { 'If-Match': String(rowVersion) } }).then(r => r.data);
export const setWorkScopes = (userId, departmentIds, rowVersion) =>
  http.put(`v1/users/${userId}/staff-profile/work-scopes`, { departmentIds }, { headers: { 'If-Match': String(rowVersion) } }).then(r => r.data);
```

- Danh sách khoa để chọn lấy từ `getFacilityTree` (Task 8 client) — chỉ khi người dùng có `facilities.read`; thiếu thì hiện "Thiếu quyền facilities.read nên không tải được danh sách khoa", vẫn hiện phạm vi hiện có (tên lấy từ DTO).

**Hành vi UI:**
- 404 `staff_profile_not_found` → "Chưa có hồ sơ nhân sự" + (có `staff-profiles.manage`) form tạo: mã nhân sự, đang làm việc.
- Có hồ sơ: hiện mã, trạng thái, danh sách phạm vi theo cơ sở → khoa. Có quyền manage: sửa mã/trạng thái; chọn khoa (checkbox nhóm theo cơ sở, chỉ khoa đang dùng) → "Lưu phạm vi".
- Cảnh báo cố định khi hồ sơ không có phạm vi hoặc đang tắt: "Người này chưa có cơ sở/khoa làm việc nên không truy cập được dữ liệu nghiệp vụ."
- Lỗi `invalid_departments` hiện danh sách khoa không hợp lệ; 412 → nạp lại hồ sơ, giữ lựa chọn để đối chiếu (mẫu `UserDetailPanel` hiện có). Không tự retry.
- State hồ sơ nhân sự giữ trong component (như dữ liệu chi tiết khác của panel); đổi user đang chọn thì bỏ response cũ (dùng `key={userId}` hoặc kiểm userId khi response về).

- [ ] **Step 1: Test client** — URL/method/body/If-Match.
- [ ] **Step 2: Test `StaffProfileSection`**: 404 → form tạo (chỉ khi có manage); tạo thành công hiện hồ sơ; chọn 2 khoa rồi lưu → `setWorkScopes(userId, [id1,id2], version)`; `invalid_departments` hiện lỗi; 412 nạp lại và giữ lựa chọn; không có `facilities.read` → thông báo thiếu quyền; hồ sơ không phạm vi → cảnh báo; đổi `userId` khi request cũ chưa về → không hiện dữ liệu của user cũ.
- [ ] **Step 3:** FAIL → implement → PASS; chạy toàn bộ `Admin` tests + eslint.
- [ ] **Step 4: Commit** `feat(fe-admin): hồ sơ nhân sự và phạm vi làm việc trong panel tài khoản`.

---

### Task 10: Nghiệm thu mốc 1

- [ ] **Step 1:** `powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Full` (từ repo root) → build, unit, integration PASS; ghi số test.
- [ ] **Step 2:** `powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Frontend` và `npm run build` trong `benh_vien_fe` → PASS.
- [ ] **Step 3:** `dotnet ef migrations has-pending-model-changes --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API` → "No changes". Đọc lại SQL 3 migration mới (`RolePermissionDefaults`, `Facilities`, `StaffProfiles`): không có cột vật lý `xmin`, backfill đúng, FK/unique đúng tên.
- [ ] **Step 4:** Kiểm DB dev (chỉ đọc, MCP DBHub nếu có): sau khởi động, vai trò `doctor` có `facilities.read` và `catalog.read` **không** có (chưa kích hoạt); Admin có `facilities.*`, `staff-profiles.*`.
- [ ] **Step 5:** Kiểm tra trình duyệt (nếu chạy được `run.ps1`; không thì ghi `NOT_RUN`): admin tạo cơ sở/khoa/phòng; tạo hồ sơ nhân sự cho một tài khoản và gán khoa; tài khoản `doctor` thấy cây cơ cấu chỉ đọc; tài khoản không quyền không thấy menu.
- [ ] **Step 6:** Cập nhật dòng "Trạng thái" cuối plan này (commit, kết quả kiểm tra, mục NOT_RUN). Commit `docs(authz): nghiệm thu plan 01 nền lớp 2`. Không push/merge.

**Trạng thái:** ĐÃ TRIỂN KHAI 2026-10-03. validate Full PASS (unit 230, IT 224 pass/8 skip), Frontend PASS (225 test), `npm run build` PASS, không pending model change. Trình duyệt: NOT_RUN. Kiểm DB dev (Step 4): NOT_RUN. Commit một lần theo yêu cầu người dùng.
