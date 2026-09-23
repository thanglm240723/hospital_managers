# Giai đoạn 2 — Domain & dữ liệu: quyền, phiên, audit, EF, seed

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Domain có đủ `Permission`/`Role`/`User` nhiều role + quyền lẻ, `SessionFamily`/`RefreshToken`, `AuditRecord`; EF Core map đủ bảng (migration `InitialIdentityAccess`); repository có khoá hàng `FOR UPDATE`; khởi động seed 9 role + danh mục quyền + Admin đầu tiên.

**Spec:** [`spec.md`](spec.md) §2 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints) · **Cần xong:** Giai đoạn 1

⚠ Task 2.3 **xoá** luồng login cũ ở Application (sẽ viết lại ở Giai đoạn 3). Từ Task 2.3 tới hết Giai đoạn 3 chưa có endpoint login — đó là chủ ý.

---

### Task 2.1: Danh mục permission, role hệ thống, entity `Permission`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Identity/PermissionDefinition.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/Permissions.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/Permission.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/SystemRoles.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Identity/PermissionsTests.cs`

**Interfaces:**
- Consumes: `Entity<TId>` (đã có).
- Produces:
  - `record PermissionDefinition(string Code, string Group, string Description)`
  - `static class Permissions` với hằng `Users.Read/Create/Activate/ManageRoles/ManagePermissions`, `Roles.Read/Manage`, `Catalog.Read`; `IReadOnlyList<PermissionDefinition> IdentityAccess`, `IReadOnlyList<PermissionDefinition> All`, `bool IsDefined(string code)`.
  - `Permission : Entity<string>` (`Id` = mã quyền), `Group`, `Description`, `static Permission Create(PermissionDefinition)`, `void Update(PermissionDefinition)`.
  - `static class SystemRoles` với 9 hằng mã role (`Admin = "admin"`, …) và `IReadOnlyList<(string Code, string Name)> All`.

- [ ] **Step 1: Viết test (đỏ)**

`tests/CleanArchCqrs.UnitTests/Domain/Identity/PermissionsTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class PermissionsTests
{
    [Fact]
    public void AllCodes_AreUniqueAndLowercaseDotSeparated()
    {
        var codes = Permissions.All.Select(p => p.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^[a-z]+(\\.[a-z]+)+$", code));
    }

    [Fact]
    public void IdentityAccess_HasTheEightSpecPermissions()
    {
        Assert.Equal(
            new[] { "permissions.read", "roles.manage", "roles.read", "users.activate", "users.create",
                    "users.permissions.manage", "users.read", "users.roles.manage" },
            Permissions.IdentityAccess.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void IsDefined_RecognisesOnlyCatalogCodes()
    {
        Assert.True(Permissions.IsDefined(Permissions.Users.Read));
        Assert.False(Permissions.IsDefined("patients.read"));
    }

    [Fact]
    public void Permission_CreateAndUpdate_CopyDefinition()
    {
        var permission = Permission.Create(new PermissionDefinition("users.read", "Tài khoản", "Xem"));
        permission.Update(new PermissionDefinition("users.read", "Tài khoản", "Xem danh sách"));

        Assert.Equal("users.read", permission.Id);
        Assert.Equal("Xem danh sách", permission.Description);
    }

    [Fact]
    public void SystemRoles_HasNineUniqueCodesIncludingAdmin()
    {
        var codes = SystemRoles.All.Select(r => r.Code).ToList();

        Assert.Equal(9, codes.Count);
        Assert.Equal(9, codes.Distinct().Count());
        Assert.Contains(SystemRoles.Admin, codes);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter PermissionsTests`
Expected: FAIL biên dịch.

- [ ] **Step 3: Viết code**

`Identity/PermissionDefinition.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity;

public sealed record PermissionDefinition(string Code, string Group, string Description);
```

`Identity/Permissions.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity;

/// Danh mục permission là code: endpoint tham chiếu hằng nên không gõ sai mã.
/// Seeder đồng bộ danh mục này vào bảng Permissions lúc khởi động.
/// Module nghiệp vụ mới: thêm class lồng + danh sách riêng, rồi nối vào All.
public static class Permissions
{
    public static class Users
    {
        public const string Read = "users.read";
        public const string Create = "users.create";
        public const string Activate = "users.activate";
        public const string ManageRoles = "users.roles.manage";
        public const string ManagePermissions = "users.permissions.manage";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Manage = "roles.manage";
    }

    public static class Catalog
    {
        public const string Read = "permissions.read";
    }

    /// Quyền của module IdentityAccess — seeder gán hết cho role admin.
    public static IReadOnlyList<PermissionDefinition> IdentityAccess { get; } =
    [
        new(Users.Read, "Tài khoản", "Xem danh sách và chi tiết tài khoản"),
        new(Users.Create, "Tài khoản", "Tạo tài khoản"),
        new(Users.Activate, "Tài khoản", "Khoá và mở khoá tài khoản"),
        new(Users.ManageRoles, "Tài khoản", "Gán vai trò cho tài khoản"),
        new(Users.ManagePermissions, "Tài khoản", "Cấp và thu hồi quyền lẻ"),
        new(Roles.Read, "Vai trò", "Xem vai trò và quyền của vai trò"),
        new(Roles.Manage, "Vai trò", "Tạo, đổi tên vai trò và sửa quyền của vai trò"),
        new(Catalog.Read, "Quyền", "Xem danh mục quyền"),
    ];

    public static IReadOnlyList<PermissionDefinition> All { get; } = [.. IdentityAccess];

    private static readonly HashSet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    public static bool IsDefined(string code) => Codes.Contains(code);
}
```

`Identity/Permission.cs`:

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity;

/// Bản ghi DB của một mục trong danh mục Permissions. Id chính là mã quyền.
public sealed class Permission : Entity<string>
{
    public string Group { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    private Permission() { }

    public static Permission Create(PermissionDefinition definition)
        => new() { Id = definition.Code, Group = definition.Group, Description = definition.Description };

    public void Update(PermissionDefinition definition)
    {
        Group = definition.Group;
        Description = definition.Description;
    }
}
```

`Identity/SystemRoles.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity;

/// 9 vai trò theo Đặc tả nghiệp vụ v2.0 — seed với IsSystem = true.
public static class SystemRoles
{
    public const string Receptionist = "receptionist";
    public const string OutpatientNurse = "outpatient-nurse";
    public const string Doctor = "doctor";
    public const string InpatientNurse = "inpatient-nurse";
    public const string LabTechnician = "lab-technician";
    public const string Pharmacist = "pharmacist";
    public const string Cashier = "cashier";
    public const string ClinicalManager = "clinical-manager";
    public const string Admin = "admin";

    public static IReadOnlyList<(string Code, string Name)> All { get; } =
    [
        (Receptionist, "Lễ tân"),
        (OutpatientNurse, "Điều dưỡng ngoại trú"),
        (Doctor, "Bác sĩ"),
        (InpatientNurse, "Điều dưỡng nội trú"),
        (LabTechnician, "KTV CLS"),
        (Pharmacist, "Dược sĩ"),
        (Cashier, "Thu ngân"),
        (ClinicalManager, "Quản lý chuyên môn"),
        (Admin, "Admin"),
    ];
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh; Domain vẫn 0 package**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter PermissionsTests && dotnet build src/CleanArchCqrs.Domain`
Expected: PASS 5/5; build 0 warning.

- [ ] **Step 5: Commit**

```bash
git add src/CleanArchCqrs.Domain/Identity tests/CleanArchCqrs.UnitTests/Domain/Identity/PermissionsTests.cs
git commit -m "feat(domain): add permission catalog and system roles"
```

---

### Task 2.2: Aggregate `Role`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Identity/Role.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/RolePermission.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Identity/RoleTests.cs`

**Interfaces:**
- Consumes: `Permissions.IsDefined` (Task 2.1), `AggregateRoot<Guid>`, `IAuditable`.
- Produces:
  - `Role : AggregateRoot<Guid>, IAuditable`: `Code`, `Name`, `IsSystem`, `IReadOnlyCollection<RolePermission> GrantedPermissions`; `static bool IsValidCode(string code)`; `static Role Create(string code, string name, bool isSystem = false)`; `void Rename(string name)`; `void SetPermissions(IEnumerable<string> permissionCodes)`.
  - `RolePermission : IAuditable`: `Guid RoleId`, `string PermissionCode`.

- [ ] **Step 1: Viết test (đỏ)**

`RoleTests.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class RoleTests
{
    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var role = Role.Create("doctor", "  Bác sĩ ", isSystem: true);

        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.Equal("doctor", role.Code);
        Assert.Equal("Bác sĩ", role.Name);
        Assert.True(role.IsSystem);
        Assert.Empty(role.GrantedPermissions);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("-leading")]
    public void Create_InvalidCode_Throws(string code)
        => Assert.Throws<ArgumentException>(() => Role.Create(code, "Tên"));

    [Fact]
    public void Create_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => Role.Create("doctor", " "));

    [Fact]
    public void SetPermissions_ReplacesWholeSetAndIgnoresDuplicates()
    {
        var role = Role.Create("tester", "Tester");
        role.SetPermissions([Permissions.Users.Read, Permissions.Roles.Read]);

        role.SetPermissions([Permissions.Roles.Read, Permissions.Catalog.Read, Permissions.Catalog.Read]);

        Assert.Equal(
            new[] { Permissions.Catalog.Read, Permissions.Roles.Read },
            role.GrantedPermissions.Select(p => p.PermissionCode).OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(role.GrantedPermissions, p => Assert.Equal(role.Id, p.RoleId));
    }

    [Fact]
    public void SetPermissions_UnknownCode_Throws()
    {
        var role = Role.Create("tester", "Tester");

        Assert.Throws<ArgumentException>(() => role.SetPermissions(["patients.read"]));
    }

    [Fact]
    public void Rename_TrimsAndRejectsEmpty()
    {
        var role = Role.Create("tester", "Tester");

        role.Rename("  Kiểm thử ");

        Assert.Equal("Kiểm thử", role.Name);
        Assert.Throws<ArgumentException>(() => role.Rename(""));
    }

    [Fact]
    public void SystemRoleCodes_AreAllValid()
        => Assert.All(SystemRoles.All, r => Assert.True(Role.IsValidCode(r.Code), r.Code));

    [Fact]
    public void RoleAndRolePermission_AreAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(Role)));
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(RolePermission)));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter RoleTests`
Expected: FAIL biên dịch.

- [ ] **Step 3: Viết code**

`Identity/RolePermission.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Domain.Identity;

public sealed class RolePermission : IAuditable
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;

    private RolePermission() { }

    internal RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }
}
```

`Identity/Role.cs`:

```csharp
using System.Text.RegularExpressions;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Domain.Identity;

public sealed class Role : AggregateRoot<Guid>, IAuditable
{
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9-]{1,49}$", RegexOptions.Compiled);

    private readonly List<RolePermission> _grantedPermissions = new();

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsSystem { get; private set; }
    public IReadOnlyCollection<RolePermission> GrantedPermissions => _grantedPermissions.AsReadOnly();

    private Role() { }

    public static bool IsValidCode(string code) => !string.IsNullOrEmpty(code) && CodePattern.IsMatch(code);

    public static Role Create(string code, string name, bool isSystem = false)
    {
        if (!IsValidCode(code))
            throw new ArgumentException("Role code must be lower-kebab-case, 2-50 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty.", nameof(name));

        return new Role { Id = Guid.CreateVersion7(), Code = code, Name = name.Trim(), IsSystem = isSystem };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty.", nameof(name));
        Name = name.Trim();
    }

    public void SetPermissions(IEnumerable<string> permissionCodes)
    {
        var target = permissionCodes.Distinct(StringComparer.Ordinal).ToList();
        foreach (var code in target)
            if (!Permissions.IsDefined(code))
                throw new ArgumentException($"Unknown permission '{code}'.", nameof(permissionCodes));

        _grantedPermissions.RemoveAll(p => !target.Contains(p.PermissionCode));
        foreach (var code in target.Where(c => _grantedPermissions.All(p => p.PermissionCode != c)))
            _grantedPermissions.Add(new RolePermission(Id, code));
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter RoleTests && dotnet build src/CleanArchCqrs.Domain`
Expected: PASS 12/12 (có 5 case Theory).

- [ ] **Step 5: Commit**

```bash
git add src/CleanArchCqrs.Domain/Identity tests/CleanArchCqrs.UnitTests/Domain/Identity/RoleTests.cs
git commit -m "feat(domain): add Role aggregate with permission set"
```

---

### Task 2.3: `User` nhiều role + quyền lẻ + SecurityVersion; token chỉ mang định danh

**Files:**
- Modify: `src/CleanArchCqrs.Domain/Identity/User.cs` (viết lại toàn bộ)
- Create: `src/CleanArchCqrs.Domain/Identity/UserRole.cs`, `src/CleanArchCqrs.Domain/Identity/UserPermission.cs`
- Modify: `src/CleanArchCqrs.Domain/Identity/Events/UserRegisteredDomainEvent.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/Events/UserRolesChangedDomainEvent.cs`, `.../UserPermissionsChangedDomainEvent.cs`
- Delete: `src/CleanArchCqrs.Domain/Constants/Roles.cs` (và thư mục `Constants/`)
- Delete: `src/CleanArchCqrs.Application/Auth/` (toàn bộ: `Commands/Login/*`, `Models/*`, `Validators/*`)
- Modify: `src/CleanArchCqrs.Application/Common/Interfaces/ITokenService.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/Security/JwtOptions.cs`, `JwtTokenService.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/UserRoleConfiguration.cs`, `UserPermissionConfiguration.cs`
- Modify: `src/CleanArchCqrs.API/appsettings.json` (`Jwt:AccessTokenMinutes`)
- Test: Create `tests/CleanArchCqrs.UnitTests/Domain/Identity/UserTests.cs`; Modify `Infrastructure/Security/JwtTokenServiceTests.cs`, `Infrastructure/Repositories/UserRepositoryTests.cs`, `Infrastructure/Persistence/AppDbContextTests.cs`, `Infrastructure/Persistence/EntityConfigurationTests.cs`, `Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptorTests.cs`

**Interfaces:**
- Consumes: `Permissions.IsDefined` (2.1).
- Produces:
  - `User`: bỏ `Role`; thêm `bool MustChangePassword`, `int SecurityVersion` (mặc định 1), `IReadOnlyCollection<UserRole> RoleAssignments`, `IReadOnlyCollection<UserPermission> PermissionGrants`; `static User Create(string fullName, string email, string passwordHash, string? avatarUrl)` (→ `MustChangePassword = true`); `void SetRoles(IReadOnlyCollection<Guid> roleIds, Guid? assignedBy, DateTimeOffset now)`; `void GrantPermission(string permissionCode, string reason, Guid? grantedBy, DateTimeOffset now)`; `void RevokePermission(string permissionCode)`; `bool HasRole(Guid roleId)`; `ChangePassword` và `Deactivate` tăng `SecurityVersion`, `ChangePassword` xoá `MustChangePassword`.
  - `UserRole : IAuditable` (`UserId`, `RoleId`, `Guid? FacilityId`, `AssignedAtUtc`, `Guid? AssignedBy`); `UserPermission : IAuditable` (`UserId`, `PermissionCode`, `Reason`, `GrantedAtUtc`, `Guid? GrantedBy`).
  - `ITokenService.CreateAccessToken(Guid userId, Guid sessionFamilyId, int securityVersion) : AccessToken` — claims `sub, fid, sv, jti, iat, nbf, exp, iss, aud`.
  - `JwtOptions.AccessTokenMinutes` mặc định **15**.

- [ ] **Step 1: Viết test `User` (đỏ)**

`UserTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Events;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Create("Nguyen Van A", "A@Example.com", "hash-1", null);

    [Fact]
    public void Create_RequiresPasswordChangeAndStartsAtSecurityVersion1()
    {
        var user = NewUser();

        Assert.True(user.MustChangePassword);
        Assert.Equal(1, user.SecurityVersion);
        Assert.Equal("a@example.com", user.Email);
        Assert.IsType<UserRegisteredDomainEvent>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void SetRoles_AddsRemovesAndOnlyRaisesWhenChanged()
    {
        var user = NewUser();
        var doctor = Guid.NewGuid();
        var cashier = Guid.NewGuid();
        user.ClearDomainEvents();

        user.SetRoles([doctor, cashier, doctor], assignedBy: null, Now);
        user.SetRoles([cashier], assignedBy: null, Now);
        user.SetRoles([cashier], assignedBy: null, Now);

        Assert.Equal(cashier, Assert.Single(user.RoleAssignments).RoleId);
        Assert.True(user.HasRole(cashier));
        Assert.False(user.HasRole(doctor));
        Assert.Equal(2, user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Count());
    }

    [Fact]
    public void GrantPermission_IsIdempotentAndRequiresKnownCodeAndReason()
    {
        var user = NewUser();

        user.GrantPermission(Permissions.Users.Read, "Hỗ trợ tra cứu", grantedBy: null, Now);
        user.GrantPermission(Permissions.Users.Read, "Lần 2", grantedBy: null, Now);

        var grant = Assert.Single(user.PermissionGrants);
        Assert.Equal("Hỗ trợ tra cứu", grant.Reason);
        Assert.Throws<ArgumentException>(() => user.GrantPermission("patients.read", "x", null, Now));
        Assert.Throws<ArgumentException>(() => user.GrantPermission(Permissions.Roles.Read, " ", null, Now));
    }

    [Fact]
    public void RevokePermission_RemovesGrant()
    {
        var user = NewUser();
        user.GrantPermission(Permissions.Users.Read, "cần", null, Now);

        user.RevokePermission(Permissions.Users.Read);

        Assert.Empty(user.PermissionGrants);
    }

    [Fact]
    public void ChangePassword_BumpsSecurityVersionAndClearsMustChange()
    {
        var user = NewUser();

        user.ChangePassword("hash-2");

        Assert.Equal(2, user.SecurityVersion);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void Deactivate_BumpsSecurityVersionOnce()
    {
        var user = NewUser();

        user.Deactivate();
        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.Equal(2, user.SecurityVersion);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter UserTests`
Expected: FAIL biên dịch (`Create` còn tham số role, chưa có `SetRoles`…).

- [ ] **Step 3: Viết Domain**

`Identity/UserRole.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Domain.Identity;

public sealed class UserRole : IAuditable
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    /// Chừa sẵn cho quyền theo cơ sở — spec này chưa dùng, luôn null.
    public Guid? FacilityId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public Guid? AssignedBy { get; private set; }

    private UserRole() { }

    internal UserRole(Guid userId, Guid roleId, Guid? assignedBy, DateTimeOffset assignedAtUtc)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedBy = assignedBy;
        AssignedAtUtc = assignedAtUtc;
    }
}
```

`Identity/UserPermission.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Domain.Identity;

/// Quyền lẻ cấp thêm ngoài role. Chỉ cấp thêm, không có deny.
public sealed class UserPermission : IAuditable
{
    public Guid UserId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public DateTimeOffset GrantedAtUtc { get; private set; }
    public Guid? GrantedBy { get; private set; }

    private UserPermission() { }

    internal UserPermission(Guid userId, string permissionCode, string reason, Guid? grantedBy, DateTimeOffset grantedAtUtc)
    {
        UserId = userId;
        PermissionCode = permissionCode;
        Reason = reason;
        GrantedBy = grantedBy;
        GrantedAtUtc = grantedAtUtc;
    }
}
```

`Events/UserRegisteredDomainEvent.cs` (bỏ `Role`):

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email) : DomainEvent;
```

`Events/UserRolesChangedDomainEvent.cs`:

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserRolesChangedDomainEvent(Guid UserId) : DomainEvent;
```

`Events/UserPermissionsChangedDomainEvent.cs`:

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserPermissionsChangedDomainEvent(Guid UserId) : DomainEvent;
```

`Identity/User.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Events;

namespace CleanArchCqrs.Domain.Identity;

public sealed class User : AggregateRoot<Guid>, IAuditable
{
    private readonly List<UserRole> _roleAssignments = new();
    private readonly List<UserPermission> _permissionGrants = new();

    public string FullName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string? AvatarUrl { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool MustChangePassword { get; private set; }
    /// Tăng khi khoá tài khoản / đổi mật khẩu. JWT mang giá trị này (claim "sv");
    /// Gateway so với Redis/DB để access token cũ chết ngay.
    public int SecurityVersion { get; private set; } = 1;
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public IReadOnlyCollection<UserRole> RoleAssignments => _roleAssignments.AsReadOnly();
    public IReadOnlyCollection<UserPermission> PermissionGrants => _permissionGrants.AsReadOnly();

    private User() { }

    private User(Guid id, string fullName, string email, string passwordHash, string? avatarUrl)
    {
        Id = id;
        FullName = fullName;
        Email = email;
        PasswordHash = passwordHash;
        AvatarUrl = avatarUrl;
        CreatedAt = DateTimeOffset.UtcNow;
        MustChangePassword = true;
    }

    /// Mật khẩu ban đầu luôn do Admin/seed đặt, nên tài khoản mới phải đổi mật khẩu ở lần đăng nhập đầu.
    public static User Create(string fullName, string email, string passwordHash, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name cannot be empty.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash cannot be empty.", nameof(passwordHash));

        var user = new User(Guid.CreateVersion7(), fullName.Trim(), NormalizeEmail(email), passwordHash, avatarUrl);
        user.Raise(new UserRegisteredDomainEvent(user.Id, user.Email));
        return user;
    }

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("New password hash cannot be empty.", nameof(newPasswordHash));
        if (newPasswordHash == PasswordHash)
            throw new InvalidOperationException("New password hash cannot be the same as the current password hash.");

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        SecurityVersion++;
        Touch();
        Raise(new UserPasswordChangedDomainEvent(Id));
    }

    public void UpdateProfile(string fullName, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name cannot be empty.", nameof(fullName));
        FullName = fullName.Trim();
        AvatarUrl = avatarUrl;
        Touch();
        Raise(new UserProfileUpdatedDomainEvent(Id));
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrEmpty(email))
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        return email.Trim().ToLowerInvariant();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        SecurityVersion++;
        Touch();
        Raise(new UserDeactivatedDomainEvent(Id));
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        Touch();
        Raise(new UserActivatedDomainEvent(Id));
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public bool HasRole(Guid roleId) => _roleAssignments.Any(r => r.RoleId == roleId);

    /// Thay nguyên tập role. Không đổi gì thì không raise event.
    public void SetRoles(IReadOnlyCollection<Guid> roleIds, Guid? assignedBy, DateTimeOffset now)
    {
        var target = roleIds.Distinct().ToList();
        var removed = _roleAssignments.RemoveAll(r => !target.Contains(r.RoleId));
        var added = 0;
        foreach (var roleId in target.Where(id => !HasRole(id)))
        {
            _roleAssignments.Add(new UserRole(Id, roleId, assignedBy, now));
            added++;
        }

        if (removed + added == 0) return;
        Touch();
        Raise(new UserRolesChangedDomainEvent(Id));
    }

    public void GrantPermission(string permissionCode, string reason, Guid? grantedBy, DateTimeOffset now)
    {
        if (!Permissions.IsDefined(permissionCode))
            throw new ArgumentException($"Unknown permission '{permissionCode}'.", nameof(permissionCode));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason cannot be empty.", nameof(reason));
        if (_permissionGrants.Any(p => p.PermissionCode == permissionCode)) return;

        _permissionGrants.Add(new UserPermission(Id, permissionCode, reason.Trim(), grantedBy, now));
        Touch();
        Raise(new UserPermissionsChangedDomainEvent(Id));
    }

    public void RevokePermission(string permissionCode)
    {
        if (_permissionGrants.RemoveAll(p => p.PermissionCode == permissionCode) == 0) return;
        Touch();
        Raise(new UserPermissionsChangedDomainEvent(Id));
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
```

Xoá `src/CleanArchCqrs.Domain/Constants/Roles.cs` và thư mục `Constants/`.

- [ ] **Step 4: Xoá luồng login cũ ở Application, đổi hợp đồng token**

Xoá cả thư mục `src/CleanArchCqrs.Application/Auth/`.

`Common/Interfaces/ITokenService.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ITokenService
{
    /// JWT chỉ mang định danh ổn định: sub, fid (SessionFamily), sv (SecurityVersion). Không role/permission.
    AccessToken CreateAccessToken(Guid userId, Guid sessionFamilyId, int securityVersion);
}
```

`Infrastructure/Security/JwtOptions.cs`: đổi `AccessTokenMinutes { get; set; } = 60;` → `= 15;`.

`Infrastructure/Security/JwtTokenService.cs` — thay method `CreateAccessToken`:

```csharp
    public AccessToken CreateAccessToken(Guid userId, Guid sessionFamilyId, int securityVersion)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = userId.ToString(),
                ["fid"] = sessionFamilyId.ToString(),
                ["sv"] = securityVersion,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
            }
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }
```

Bỏ `using CleanArchCqrs.Domain.Identity;` trong file này nếu không còn dùng.

`API/appsettings.json`: `"AccessTokenMinutes": 60` → `15`.

- [ ] **Step 5: Cấu hình EF cho collection mới**

`UserConfiguration.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.HasMany(u => u.RoleAssignments).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(u => u.PermissionGrants).WithOne().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(u => u.DomainEvents);
    }
}
```

`UserRoleConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(r => new { r.UserId, r.RoleId });
    }
}
```

`UserPermissionConfiguration.cs`:

```csharp
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
```

- [ ] **Step 6: Sửa test cũ theo chữ ký mới**

Trong `UserRepositoryTests.cs`, `AppDbContextTests.cs`, `EntityConfigurationTests.cs`, `AuditSaveChangesInterceptorTests.cs`:
- xoá `using CleanArchCqrs.Domain.Constants;`
- mọi `User.Create(a, b, c, null, Roles.Admin)` → `User.Create(a, b, c, null)`.

Trong `EntityConfigurationTests.UserConfiguration_SetsMaxLengthsAndIgnoresDomainEvents`, thay dòng assert `User.Role` bằng:

```csharp
        Assert.NotNull(entityType.FindNavigation(nameof(User.RoleAssignments)));
```

Trong `AuditSaveChangesInterceptorTests.cs`, `ProbeDbContext.OnModelCreating` thành (User giờ có navigation tới 2 entity khoá kép):

```csharp
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Ignore(u => u.DomainEvents);
        modelBuilder.Entity<UserRole>().HasKey(r => new { r.UserId, r.RoleId });
        modelBuilder.Entity<UserPermission>().HasKey(p => new { p.UserId, p.PermissionCode });
    }
```

`JwtTokenServiceTests.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_CarriesOnlyIdentityClaims()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 32),
        }));
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var token = service.CreateAccessToken(userId, familyId, securityVersion: 3);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(familyId.ToString(), jwt.GetClaim("fid").Value);
        Assert.Equal("3", jwt.GetClaim("sv").Value);
        Assert.Equal(
            new[] { "aud", "exp", "fid", "iat", "iss", "jti", "nbf", "sub", "sv" },
            jwt.Claims.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        Assert.InRange(token.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
    }
}
```

- [ ] **Step 7: Chạy toàn bộ unit test**

Run: `dotnet build && dotnet test tests/CleanArchCqrs.UnitTests`
Expected: build 0 warning; tất cả PASS (gồm `UserTests` 6/6 và `JwtTokenServiceTests`).

- [ ] **Step 8: Commit**

```bash
git add -A src tests/CleanArchCqrs.UnitTests
git commit -m "feat(domain)!: users hold many roles and permission grants; JWT carries only sub/fid/sv"
```

---

### Task 2.4: `SessionFamily` và `RefreshToken`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Identity/Sessions/SessionStatus.cs`, `SessionRevokeReason.cs`, `RotationResult.cs`, `RefreshToken.cs`, `SessionFamily.cs`, `ISessionRepository.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Identity/SessionFamilyTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<Guid>`, `Entity<Guid>`.
- Produces (namespace `CleanArchCqrs.Domain.Identity.Sessions`):
  - `enum SessionStatus { Active, Revoked }`; `enum SessionRevokeReason { Logout, LogoutAll, Reuse, UserRevoked, PasswordChanged, AccountDeactivated }`; `enum RotationResult { Rotated, ReuseDetected, Expired, NotActive }`.
  - `RefreshToken : Entity<Guid>`: `FamilyId`, `TokenHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc?`, `RevokedAtUtc?`, `ReplacedById?`, `bool IsUsable`.
  - `SessionFamily : AggregateRoot<Guid>` (**không** `IAuditable`): `static readonly TimeSpan AbsoluteLifetime` (7 ngày), `UserId`, `Status`, `CreatedAtUtc`, `AbsoluteExpiresAtUtc`, `RevokedAtUtc?`, `RevokeReason?`, `IpAddress?`, `UserAgent?`, `LastRefreshedAtUtc?`, `IReadOnlyCollection<RefreshToken> Tokens`; `static SessionFamily Start(Guid userId, string tokenHash, DateTimeOffset now, string? ipAddress, string? userAgent)`; `RotationResult Rotate(string presentedTokenHash, string newTokenHash, DateTimeOffset now)`; `void Revoke(SessionRevokeReason reason, DateTimeOffset now)`; `bool IsActiveAt(DateTimeOffset now)`.
  - `ISessionRepository`: `Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default)`; `Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default)` (khoá family → token; nạp token được trình + token còn dùng được; **phải gọi trong transaction**); `Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default)`; `Task AddAsync(SessionFamily family, CancellationToken ct = default)`.

- [ ] **Step 1: Viết test (đỏ)**

`SessionFamilyTests.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class SessionFamilyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static SessionFamily NewFamily() => SessionFamily.Start(Guid.NewGuid(), "h1", T0, "10.0.0.1", "UA");

    [Fact]
    public void Start_CreatesActiveFamilyWithOneTokenAndSevenDayLimit()
    {
        var family = NewFamily();

        Assert.Equal(SessionStatus.Active, family.Status);
        Assert.Equal(T0.AddDays(7), family.AbsoluteExpiresAtUtc);
        var token = Assert.Single(family.Tokens);
        Assert.Equal("h1", token.TokenHash);
        Assert.Equal(family.AbsoluteExpiresAtUtc, token.ExpiresAtUtc);
        Assert.True(token.IsUsable);
    }

    [Fact]
    public void Rotate_ConsumesOldTokenAndIssuesNewOneWithoutExtendingFamily()
    {
        var family = NewFamily();
        var later = T0.AddMinutes(14);

        var result = family.Rotate("h1", "h2", later);

        Assert.Equal(RotationResult.Rotated, result);
        var old = family.Tokens.Single(t => t.TokenHash == "h1");
        var next = family.Tokens.Single(t => t.TokenHash == "h2");
        Assert.Equal(later, old.ConsumedAtUtc);
        Assert.Equal(next.Id, old.ReplacedById);
        Assert.Equal(family.AbsoluteExpiresAtUtc, next.ExpiresAtUtc);
        Assert.Equal(later, family.LastRefreshedAtUtc);
    }

    [Fact]
    public void Rotate_ConsumedTokenPresentedAgain_RevokesWholeFamily()
    {
        var family = NewFamily();
        family.Rotate("h1", "h2", T0.AddMinutes(1));

        var result = family.Rotate("h1", "h3", T0.AddMinutes(2));

        Assert.Equal(RotationResult.ReuseDetected, result);
        Assert.Equal(SessionStatus.Revoked, family.Status);
        Assert.Equal(SessionRevokeReason.Reuse, family.RevokeReason);
        Assert.False(family.Tokens.Single(t => t.TokenHash == "h2").IsUsable);
        Assert.DoesNotContain(family.Tokens, t => t.TokenHash == "h3");
    }

    [Fact]
    public void Rotate_AfterAbsoluteExpiry_ReturnsExpired()
    {
        var family = NewFamily();

        Assert.Equal(RotationResult.Expired, family.Rotate("h1", "h2", T0.AddDays(7)));
    }

    [Fact]
    public void Rotate_RevokedFamily_ReturnsNotActive()
    {
        var family = NewFamily();
        family.Revoke(SessionRevokeReason.Logout, T0.AddMinutes(1));

        Assert.Equal(RotationResult.NotActive, family.Rotate("h1", "h2", T0.AddMinutes(2)));
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsFirstReason()
    {
        var family = NewFamily();

        family.Revoke(SessionRevokeReason.Logout, T0.AddMinutes(1));
        family.Revoke(SessionRevokeReason.LogoutAll, T0.AddMinutes(2));

        Assert.Equal(SessionRevokeReason.Logout, family.RevokeReason);
        Assert.Equal(T0.AddMinutes(1), family.RevokedAtUtc);
        Assert.False(Assert.Single(family.Tokens).IsUsable);
        Assert.False(family.IsActiveAt(T0.AddMinutes(3)));
    }

    [Fact]
    public void SessionFamily_IsNotAuditable()
        => Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(SessionFamily)));
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter SessionFamilyTests`
Expected: FAIL biên dịch.

- [ ] **Step 3: Viết code**

`Sessions/SessionStatus.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity.Sessions;

public enum SessionStatus
{
    Active,
    Revoked
}
```

`Sessions/SessionRevokeReason.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity.Sessions;

public enum SessionRevokeReason
{
    Logout,
    LogoutAll,
    Reuse,
    UserRevoked,
    PasswordChanged,
    AccountDeactivated
}
```

`Sessions/RotationResult.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity.Sessions;

public enum RotationResult
{
    Rotated,
    ReuseDetected,
    Expired,
    NotActive
}
```

`Sessions/RefreshToken.cs`:

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Sessions;

/// Chỉ lưu hash SHA-256 của refresh token, không bao giờ lưu token gốc.
public sealed class RefreshToken : Entity<Guid>
{
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsUsable => ConsumedAtUtc is null && RevokedAtUtc is null;

    private RefreshToken() { }

    internal static RefreshToken Issue(Guid familyId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAtUtc)
        => new()
        {
            Id = Guid.CreateVersion7(),
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };

    internal void MarkConsumed(DateTimeOffset now, Guid replacedById)
    {
        ConsumedAtUtc = now;
        ReplacedById = replacedById;
    }

    internal void MarkRevoked(DateTimeOffset now)
    {
        if (IsUsable) RevokedAtUtc = now;
    }
}
```

`Sessions/SessionFamily.cs`:

```csharp
using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Sessions;

/// Một lần đăng nhập = một family. Mọi refresh token xoay vòng trong family đều chung hạn tuyệt đối.
public sealed class SessionFamily : AggregateRoot<Guid>
{
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    private readonly List<RefreshToken> _tokens = new();

    public Guid UserId { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public SessionRevokeReason? RevokeReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset? LastRefreshedAtUtc { get; private set; }
    /// Chỉ chứa các token repository đã nạp (token được trình + token còn dùng được), không phải toàn bộ lịch sử.
    public IReadOnlyCollection<RefreshToken> Tokens => _tokens.AsReadOnly();

    private SessionFamily() { }

    public static SessionFamily Start(Guid userId, string tokenHash, DateTimeOffset now, string? ipAddress, string? userAgent)
    {
        var family = new SessionFamily
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = SessionStatus.Active,
            CreatedAtUtc = now,
            AbsoluteExpiresAtUtc = now + AbsoluteLifetime,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
        family._tokens.Add(RefreshToken.Issue(family.Id, tokenHash, now, family.AbsoluteExpiresAtUtc));
        return family;
    }

    public bool IsActiveAt(DateTimeOffset now) => Status == SessionStatus.Active && now < AbsoluteExpiresAtUtc;

    /// Strict reuse: token đã dùng/đã thu hồi mà được trình lại ⇒ thu hồi cả family.
    public RotationResult Rotate(string presentedTokenHash, string newTokenHash, DateTimeOffset now)
    {
        if (Status != SessionStatus.Active) return RotationResult.NotActive;
        if (now >= AbsoluteExpiresAtUtc) return RotationResult.Expired;

        var presented = _tokens.SingleOrDefault(t => t.TokenHash == presentedTokenHash)
            ?? throw new InvalidOperationException("The presented refresh token was not loaded into this family.");

        if (!presented.IsUsable)
        {
            Revoke(SessionRevokeReason.Reuse, now);
            return RotationResult.ReuseDetected;
        }

        var next = RefreshToken.Issue(Id, newTokenHash, now, AbsoluteExpiresAtUtc);
        presented.MarkConsumed(now, next.Id);
        _tokens.Add(next);
        LastRefreshedAtUtc = now;
        return RotationResult.Rotated;
    }

    public void Revoke(SessionRevokeReason reason, DateTimeOffset now)
    {
        if (Status == SessionStatus.Revoked) return;
        Status = SessionStatus.Revoked;
        RevokedAtUtc = now;
        RevokeReason = reason;
        foreach (var token in _tokens) token.MarkRevoked(now);
    }
}
```

`Sessions/ISessionRepository.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity.Sessions;

public interface ISessionRepository
{
    /// Tra không khoá, không tracking — chỉ để biết family nào cần khoá.
    Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// Khoá hàng family rồi hàng token (FOR UPDATE), nạp token được trình và token còn dùng được.
    /// PHẢI gọi bên trong transaction (IUnitOfWork.BeginTransactionAsync), nếu không khoá nhả ngay.
    Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default);

    /// Khoá mọi family Active của user theo thứ tự Id, nạp token còn dùng được. Gọi trong transaction.
    Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(SessionFamily family, CancellationToken ct = default);
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter SessionFamilyTests && dotnet build src/CleanArchCqrs.Domain`
Expected: PASS 7/7.

- [ ] **Step 5: Commit**

```bash
git add src/CleanArchCqrs.Domain/Identity/Sessions tests/CleanArchCqrs.UnitTests/Domain/Identity/SessionFamilyTests.cs
git commit -m "feat(domain): add SessionFamily with strict-reuse refresh token rotation"
```

---

### Task 2.5: Audit truy cập (`AuditRecord`), CorrelationId, bỏ `UserLoginHistory`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Common/Auditing/AuditResult.cs`, `AuditRecord.cs`
- Modify: `src/CleanArchCqrs.Domain/Common/Auditing/AuditLog.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IRequestContext.cs`
- Create: `src/CleanArchCqrs.API/Services/HttpRequestContext.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/Persistence/AppDbContext.cs`, `DependencyInjection/InfrastructureServiceExtensions.cs`
- Delete: `src/CleanArchCqrs.Domain/Identity/UserLoginHistory.cs`, `IUserLoginHistoryRepository.cs`; `src/CleanArchCqrs.Infrastructure/Repositories/UserLoginHistoryRepository.cs`; `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/UserLoginHistoryConfiguration.cs`
- Test: Create `tests/CleanArchCqrs.UnitTests/Domain/Common/Auditing/AuditRecordTests.cs`, `tests/CleanArchCqrs.UnitTests/API/Services/HttpRequestContextTests.cs`; Modify `AuditLogTests.cs`, `AuditSaveChangesInterceptorTests.cs`, `EntityConfigurationTests.cs`, `InfrastructureServiceExtensionsTests.cs`; Delete `Domain/Identity/UserLoginHistoryTests.cs`, `Infrastructure/Repositories/UserLoginHistoryRepositoryTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser.UserId` (đã có).
- Produces:
  - `enum AuditResult { Succeeded, Failed, Denied }`.
  - `AuditRecord : Entity<Guid>` (không `IAuditable`): `ActorId?`, `Action`, `ResourceType?`, `ResourceId?`, `Result`, `Reason?`, `CorrelationId?`, `IpAddress?`, `UserAgent?`, `TimestampUtc`, `Metadata?`; `static AuditRecord Create(Guid? actorId, string action, AuditResult result, string? reason, string? resourceType, string? resourceId, string? correlationId, string? ipAddress, string? userAgent, string? metadataJson, DateTimeOffset timestampUtc)`.
  - `AuditLog.CorrelationId` + tham số cuối `string? correlationId = null` ở `AuditLog.Create`.
  - `IRequestContext { string? CorrelationId; string? IpAddress; string? UserAgent; }` — `UserAgent` đã cắt ≤ 512 ký tự.
  - `AuditSaveChangesInterceptor(ICurrentUser, IRequestContext)`; `EntityId` = giá trị khoá chính nối bằng `,` (hỗ trợ khoá kép); bỏ qua property `PasswordHash`, `TokenHash`.

- [ ] **Step 1: Viết test (đỏ)**

`Domain/Common/Auditing/AuditRecordTests.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Common.Auditing;

public class AuditRecordTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var actor = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

        var record = AuditRecord.Create(actor, "auth.login", AuditResult.Failed, "InvalidPassword", "User", "u-1",
            "corr-1", "10.0.0.1", "UA", "{\"k\":1}", at);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(actor, record.ActorId);
        Assert.Equal("auth.login", record.Action);
        Assert.Equal(AuditResult.Failed, record.Result);
        Assert.Equal("InvalidPassword", record.Reason);
        Assert.Equal("corr-1", record.CorrelationId);
        Assert.Equal(at, record.TimestampUtc);
    }

    [Fact]
    public void Create_EmptyAction_Throws()
        => Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(null, " ", AuditResult.Succeeded, null, null, null, null, null, null, null, DateTimeOffset.UtcNow));

    [Fact]
    public void AuditRecord_IsNotAuditable()
        => Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(AuditRecord)));
}
```

Trong `AuditLogTests.cs` thêm:

```csharp
    [Fact]
    public void Create_StoresCorrelationId()
    {
        var log = AuditLog.Create("User", "abc-123", AuditAction.Updated, null, "{}", "corr-9");

        Assert.Equal("corr-9", log.CorrelationId);
    }
```

`API/Services/HttpRequestContextTests.cs`:

```csharp
using System.Net;
using CleanArchCqrs.API.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Services;

public class HttpRequestContextTests
{
    [Fact]
    public void ReadsCorrelationIpAndTruncatedUserAgent()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Correlation-Id"] = "corr-1";
        http.Request.Headers.UserAgent = new string('u', 600);
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");

        var context = new HttpRequestContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal("corr-1", context.CorrelationId);
        Assert.Equal("10.1.2.3", context.IpAddress);
        Assert.Equal(512, context.UserAgent!.Length);
    }

    [Fact]
    public void NoHttpContext_ReturnsNulls()
    {
        var context = new HttpRequestContext(new HttpContextAccessor());

        Assert.Null(context.CorrelationId);
        Assert.Null(context.IpAddress);
        Assert.Null(context.UserAgent);
    }
}
```

Trong `AuditSaveChangesInterceptorTests.cs`:
- thêm fake và entity khoá kép ngay dưới `FakeCurrentUser`:

```csharp
sealed class FakeRequestContext : IRequestContext
{
    public string? CorrelationId { get; init; }
    public string? IpAddress => null;
    public string? UserAgent => null;
}

sealed class ProbeLink : IAuditable
{
    public Guid LeftId { get; set; }
    public string RightCode { get; set; } = "";
}
```

- `ProbeDbContext` thêm `public DbSet<ProbeLink> Links => Set<ProbeLink>();` và trong `OnModelCreating` thêm `modelBuilder.Entity<ProbeLink>().HasKey(l => new { l.LeftId, l.RightCode });`
- `CreateContext` tạo interceptor bằng `new AuditSaveChangesInterceptor(new FakeCurrentUser { UserId = currentUserId }, new FakeRequestContext { CorrelationId = "corr-test" })`
- thêm 2 test:

```csharp
    [Fact]
    public async Task SaveChanges_CompositeKeyEntity_UsesJoinedKeyAsEntityId()
    {
        using var context = CreateContext(Guid.NewGuid());
        var leftId = Guid.NewGuid();

        context.Links.Add(new ProbeLink { LeftId = leftId, RightCode = "users.read" });
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Equal(nameof(ProbeLink), log.EntityName);
        Assert.Equal($"{leftId},users.read", log.EntityId);
    }

    [Fact]
    public async Task SaveChanges_RecordsCorrelationId()
    {
        using var context = CreateContext(Guid.NewGuid());

        context.Users.Add(User.Create("A", "corr@example.com", "hash", null));
        await context.SaveChangesAsync();

        Assert.Equal("corr-test", Assert.Single(context.AuditLogs).CorrelationId);
    }
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests`
Expected: FAIL biên dịch (`AuditRecord`, `IRequestContext`, `HttpRequestContext` chưa có).

- [ ] **Step 3: Domain audit**

`Common/Auditing/AuditResult.cs`:

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

public enum AuditResult
{
    Succeeded,
    Failed,
    Denied
}
```

`Common/Auditing/AuditRecord.cs`:

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

/// Audit truy cập/bảo mật (Đặc tả kỹ thuật §3.3). Chỉ insert. KHÔNG implement IAuditable.
public sealed class AuditRecord : Entity<Guid>
{
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public AuditResult Result { get; private set; }
    public string? Reason { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }
    public string? Metadata { get; private set; }

    private AuditRecord() { }

    public static AuditRecord Create(Guid? actorId, string action, AuditResult result, string? reason,
        string? resourceType, string? resourceId, string? correlationId, string? ipAddress, string? userAgent,
        string? metadataJson, DateTimeOffset timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty.", nameof(action));

        return new AuditRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = actorId,
            Action = action,
            Result = result,
            Reason = reason,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = metadataJson,
            TimestampUtc = timestampUtc
        };
    }
}
```

`Common/Auditing/AuditLog.cs` — thêm property và tham số:

```csharp
    public string? CorrelationId { get; private set; }
```

```csharp
    public static AuditLog Create(string entityName, string entityId, AuditAction action,
        Guid? changedByUserId, string changesJson, string? correlationId = null)
```

và trong object initializer thêm `CorrelationId = correlationId`.

- [ ] **Step 4: `IRequestContext` và bản cài HTTP**

`Application/Common/Interfaces/IRequestContext.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces;

/// Thông tin kỹ thuật của request hiện tại cho audit. Ngoài HTTP (worker, seed) mọi giá trị là null.
public interface IRequestContext
{
    string? CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
```

`API/Services/HttpRequestContext.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Services;

public sealed class HttpRequestContext : IRequestContext
{
    private const int MaxUserAgentLength = 512;
    private readonly IHttpContextAccessor _accessor;

    public HttpRequestContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? CorrelationId => NullIfEmpty(_accessor.HttpContext?.Request.Headers["X-Correlation-Id"].ToString());

    /// Sau UseForwardedHeaders đây là IP client thật do Gateway chuyển xuống.
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = NullIfEmpty(_accessor.HttpContext?.Request.Headers.UserAgent.ToString());
            return value is { Length: > MaxUserAgentLength } ? value[..MaxUserAgentLength] : value;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
```

`Program.cs`: ngay dưới `builder.Services.AddScoped<ICurrentUser, CurrentUser>();` thêm:

```csharp
        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
```

- [ ] **Step 5: Interceptor**

`AuditSaveChangesInterceptor.cs` (toàn bộ):

```csharp
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchCqrs.Infrastructure.Persistence.Interceptors;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal) { "PasswordHash", "TokenHash" };

    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser, IRequestContext requestContext)
    {
        _currentUser = currentUser;
        _requestContext = requestContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                     && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList())
        {
            var changes = new Dictionary<string, object?>();
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                _ => AuditAction.Deleted
            };

            foreach (var p in entry.Properties.Where(p => !ExcludedProperties.Contains(p.Metadata.Name)))
            {
                switch (action)
                {
                    case AuditAction.Created:
                        changes[p.Metadata.Name] = new { New = p.CurrentValue };
                        break;
                    case AuditAction.Updated when p.IsModified:
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue, New = p.CurrentValue };
                        break;
                    case AuditAction.Deleted:
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue };
                        break;
                }
            }

            if (action == AuditAction.Updated && changes.Count == 0) continue;

            context.Set<AuditLog>().Add(AuditLog.Create(
                entry.Metadata.ClrType.Name, KeyOf(entry), action, _currentUser.UserId,
                JsonSerializer.Serialize(changes), _requestContext.CorrelationId));
        }
    }

    /// Khoá kép (UserRoles, RolePermissions…) nối bằng dấu phẩy theo thứ tự khai báo khoá.
    private static string KeyOf(EntityEntry entry)
        => string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
}
```

- [ ] **Step 6: Gỡ `UserLoginHistory`**

- Xoá 4 file nguồn ở mục **Files** và 2 file test tương ứng.
- `AppDbContext.cs`: xoá dòng `DbSet<UserLoginHistory> UserLoginHistories`.
- `InfrastructureServiceExtensions.cs`: xoá dòng `services.AddScoped<IUserLoginHistoryRepository, UserLoginHistoryRepository>();`.
- `EntityConfigurationTests.cs`: xoá test `UserLoginHistoryConfiguration_SetsMaxLengthsAndIndexes`.
- `InfrastructureServiceExtensionsTests.cs`: xoá dòng assert `IUserLoginHistoryRepository`; thêm fake và đăng ký nó (interceptor giờ cần `IRequestContext`):

```csharp
file sealed class FakeRequestContext : IRequestContext
{
    public string? CorrelationId => null;
    public string? IpAddress => null;
    public string? UserAgent => null;
}
```

```csharp
        services.AddScoped<IRequestContext, FakeRequestContext>();
```

(đặt ngay dưới `services.AddScoped<ICurrentUser, FakeCurrentUser>();`).

- [ ] **Step 7: Chạy toàn bộ unit test**

Run: `dotnet build && dotnet test tests/CleanArchCqrs.UnitTests`
Expected: build 0 warning; PASS toàn bộ.

- [ ] **Step 8: Commit**

```bash
git add -A src tests/CleanArchCqrs.UnitTests
git commit -m "feat(audit): add AuditRecord, correlation id on AuditLog, composite-key auditing; drop UserLoginHistory"
```

---

### Task 2.6: Ánh xạ EF, transaction, migration `InitialIdentityAccess`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Common/IUnitOfWorkTransaction.cs`; Modify `src/CleanArchCqrs.Domain/Common/IUnitOfWork.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Caching/CacheInvalidation.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/AppDbTransaction.cs`; Modify `AppDbContext.cs`
- Create in `Persistence/Configurations/`: `RoleConfiguration.cs`, `RolePermissionConfiguration.cs`, `PermissionConfiguration.cs`, `SessionFamilyConfiguration.cs`, `RefreshTokenConfiguration.cs`, `AuditRecordConfiguration.cs`, `CacheInvalidationConfiguration.cs`
- Modify: `UserRoleConfiguration.cs`, `UserPermissionConfiguration.cs`, `AuditLogConfiguration.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Migrations/*` (sinh bằng `dotnet ef`)
- Test: Create `tests/CleanArchCqrs.IntegrationTests/Helpers/TestDb.cs`, `tests/CleanArchCqrs.IntegrationTests/Persistence/PersistenceTests.cs`

**Interfaces:**
- Consumes: mọi entity ở Task 2.1–2.5.
- Produces:
  - `IUnitOfWork.BeginTransactionAsync(CancellationToken ct = default) : Task<IUnitOfWorkTransaction>`; `IUnitOfWorkTransaction : IAsyncDisposable { Task CommitAsync(CancellationToken ct = default); }` — dispose khi chưa commit = rollback.
  - `AppDbContext`: `DbSet` `Users`, `Roles`, `Permissions`, `SessionFamilies`, `RefreshTokens`, `AuditLogs`, `AuditRecords`, `CacheInvalidations`. Bảng `UserRoles`, `UserPermissions`, `RolePermissions` truy cập qua `Set<T>()`.
  - `CacheInvalidation` (Infrastructure): `Id`, `Key`, `CreatedAtUtc`, `Attempts`, `LastError?`; `static Create(string key, DateTimeOffset now)`; `void MarkFailed(string error)`.
  - `TestDb.CreateMigratedDatabaseAsync(ContainersFixture) : Task<string>`, `TestDb.Create(string connectionString) : AppDbContext` (không interceptor).

- [ ] **Step 1: Viết test tích hợp (đỏ)**

`Helpers/TestDb.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.IntegrationTests.Helpers;

public static class TestDb
{
    public static async Task<string> CreateMigratedDatabaseAsync(ContainersFixture containers)
    {
        var connectionString = await containers.CreateDatabaseAsync();
        await using var db = Create(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static AppDbContext Create(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
}
```

`Persistence/PersistenceTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public PersistenceTests(ContainersFixture containers) => _containers = containers;

    private static string NewEmail() => $"u-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Migration_CreatesIdentityTablesAndDropsLoginHistory()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        await using var db = TestDb.Create(cs);

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        foreach (var table in new[] { "Users", "Roles", "Permissions", "RolePermissions", "UserRoles", "UserPermissions",
                     "SessionFamilies", "RefreshTokens", "AuditLogs", "AuditRecords", "CacheInvalidations" })
            Assert.Contains(table, tables);
        Assert.DoesNotContain("UserLoginHistories", tables);
    }

    [Fact]
    public async Task UserWithRolesAndGrants_RoundTrips()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var permission = Permission.Create(Permissions.IdentityAccess[0]);
        var role = Role.Create("tester", "Tester");
        role.SetPermissions([permission.Id]);
        var user = User.Create("A", NewEmail(), "hash", null);
        user.SetRoles([role.Id], null, Now);
        user.GrantPermission(permission.Id, "cần", null, Now);

        await using (var db = TestDb.Create(cs))
        {
            db.Permissions.Add(permission);
            db.Roles.Add(role);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
        {
            var loaded = await db.Users.Include(u => u.RoleAssignments).Include(u => u.PermissionGrants)
                .SingleAsync(u => u.Id == user.Id);
            Assert.Equal(role.Id, Assert.Single(loaded.RoleAssignments).RoleId);
            Assert.Equal(permission.Id, Assert.Single(loaded.PermissionGrants).PermissionCode);
            Assert.Single((await db.Roles.Include(r => r.GrantedPermissions).SingleAsync(r => r.Id == role.Id)).GrantedPermissions);
        }
    }

    [Fact]
    public async Task RotatedToken_AddedThroughNavigation_IsInserted()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", NewEmail(), "hash", null);
        var family = SessionFamily.Start(user.Id, "hash-1", Now, null, null);
        await using (var db = TestDb.Create(cs))
        {
            db.Users.Add(user);
            db.SessionFamilies.Add(family);
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
        {
            var loaded = await db.SessionFamilies.Include(f => f.Tokens).SingleAsync(f => f.Id == family.Id);
            Assert.Equal(RotationResult.Rotated, loaded.Rotate("hash-1", "hash-2", Now.AddMinutes(1)));
            await db.SaveChangesAsync();   // ném DbUpdateConcurrencyException nếu thiếu ValueGeneratedNever
        }

        await using (var db = TestDb.Create(cs))
            Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.FamilyId == family.Id));
    }

    [Fact]
    public async Task RefreshTokenHash_IsUnique()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", NewEmail(), "hash", null);
        await using var db = TestDb.Create(cs);
        db.Users.Add(user);
        db.SessionFamilies.Add(SessionFamily.Start(user.Id, "same-hash", Now, null, null));
        db.SessionFamilies.Add(SessionFamily.Start(user.Id, "same-hash", Now, null, null));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Transaction_DisposedWithoutCommit_RollsBack()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var email = NewEmail();

        await using (var db = TestDb.Create(cs))
        await using (await db.BeginTransactionAsync())
        {
            db.Users.Add(User.Create("A", email, "hash", null));
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter PersistenceTests`
Expected: FAIL biên dịch (`BeginTransactionAsync`, `DbSet Roles`… chưa có).

- [ ] **Step 3: Transaction**

`Domain/Common/IUnitOfWorkTransaction.cs`:

```csharp
namespace CleanArchCqrs.Domain.Common;

/// Dispose mà chưa CommitAsync ⇒ rollback.
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
```

`Domain/Common/IUnitOfWork.cs` — thêm method:

```csharp
		/// Cần khi phải khoá hàng (SELECT ... FOR UPDATE) hoặc commit trước khi trả lỗi.
		Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);
```

`Infrastructure/Persistence/AppDbTransaction.cs`:

```csharp
using CleanArchCqrs.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace CleanArchCqrs.Infrastructure.Persistence;

internal sealed class AppDbTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _inner;

    public AppDbTransaction(IDbContextTransaction inner) => _inner = inner;

    public Task CommitAsync(CancellationToken ct = default) => _inner.CommitAsync(ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
```

`Infrastructure/Caching/CacheInvalidation.cs`:

```csharp
namespace CleanArchCqrs.Infrastructure.Caching;

/// Lệnh xoá key Redis chờ thực hiện. Ghi cùng transaction nghiệp vụ nên không bao giờ thất lạc.
public sealed class CacheInvalidation
{
    private const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public string Key { get; private set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private CacheInvalidation() { }

    public static CacheInvalidation Create(string key, DateTimeOffset now)
        => new() { Id = Guid.CreateVersion7(), Key = key, CreatedAtUtc = now };

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}
```

`AppDbContext.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Persistence;

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
```

- [ ] **Step 4: Cấu hình EF**

`RoleConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.HasMany(r => r.GrantedPermissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(r => r.DomainEvents);
    }
}
```

`RolePermissionConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.PermissionCode });
        builder.Property(p => p.PermissionCode).HasMaxLength(100);
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionCode).OnDelete(DeleteBehavior.Restrict);
    }
}
```

`PermissionConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("Code").HasMaxLength(100).ValueGeneratedNever();
        builder.Property(p => p.Group).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(300);
    }
}
```

`SessionFamilyConfiguration.cs`:

```csharp
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
```

`RefreshTokenConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        // Bắt buộc: token mới được thêm qua navigation của family đã tracking.
        // Thiếu dòng này EF coi nó là Modified (vì Id đã có giá trị) và SaveChanges thất bại.
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Ignore(t => t.IsUsable);
    }
}
```

`AuditRecordConfiguration.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Reason).HasMaxLength(200);
        builder.Property(a => a.ResourceType).HasMaxLength(100);
        builder.Property(a => a.ResourceId).HasMaxLength(100);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.Metadata).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.ActorId, a.TimestampUtc });
        builder.HasIndex(a => new { a.Action, a.TimestampUtc });
    }
}
```

`CacheInvalidationConfiguration.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class CacheInvalidationConfiguration : IEntityTypeConfiguration<CacheInvalidation>
{
    public void Configure(EntityTypeBuilder<CacheInvalidation> builder)
    {
        builder.ToTable("CacheInvalidations");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Key).IsRequired().HasMaxLength(200);
        builder.Property(c => c.LastError).HasMaxLength(1000);
        builder.HasIndex(c => c.CreatedAtUtc);
    }
}
```

Sửa `UserRoleConfiguration.Configure` — thêm cuối:

```csharp
        builder.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Restrict);
```

Sửa `UserPermissionConfiguration.Configure` — thêm cuối:

```csharp
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionCode).OnDelete(DeleteBehavior.Restrict);
```

Sửa `AuditLogConfiguration.Configure` — thêm:

```csharp
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
```

- [ ] **Step 5: Sinh migration**

```bash
dotnet tool update --global dotnet-ef --version 10.0.12
dotnet ef migrations add InitialIdentityAccess --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API --output-dir Persistence/Migrations
```

Expected: có `Persistence/Migrations/<timestamp>_InitialIdentityAccess.cs` + `AppDbContextModelSnapshot.cs`. Mở file migration kiểm tra nhanh: 11 bảng, unique index `IX_RefreshTokens_TokenHash`, `IX_Users_Email`, `IX_Roles_Code`; cột `Permissions.Code`; không có `UserLoginHistories`.

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter PersistenceTests && dotnet test tests/CleanArchCqrs.UnitTests`
Expected: PASS 5/5 và unit test vẫn xanh (test SQLite unique email tạo được schema mới).

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -m "feat(persistence): map identity, session and audit tables; add unit-of-work transactions and initial migration"
```

---

### Task 2.7: Repository — user, role, session (khoá hàng)

**Files:**
- Modify: `src/CleanArchCqrs.Domain/Identity/IUserRepository.cs`, `src/CleanArchCqrs.Infrastructure/Repositories/UserRepository.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/IRoleRepository.cs`, `src/CleanArchCqrs.Infrastructure/Repositories/RoleRepository.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Repositories/SessionRepository.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Test: Create `tests/CleanArchCqrs.IntegrationTests/Persistence/RepositoryTests.cs`, `tests/CleanArchCqrs.IntegrationTests/Persistence/SessionRepositoryTests.cs`

**Interfaces:**
- Consumes: `ISessionRepository` (2.4), `AppDbContext` + `BeginTransactionAsync` (2.6).
- Produces:
  - `IUserRepository` thêm: `Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default)` (kèm `RoleAssignments`, `PermissionGrants`, tracking); `Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default)`; `Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default)`.
  - `IRoleRepository`: `GetByIdAsync(Guid id, ct)`, `GetByCodeAsync(string code, ct)` (kèm `GrantedPermissions`), `GetByIdsAsync(IReadOnlyCollection<Guid> ids, ct) : Task<IReadOnlyList<Role>>`, `CodeExistsAsync(string code, ct)`, `AddAsync(Role role, ct)`.
  - `SessionRepository : ISessionRepository` (khoá bằng `SELECT 1 … FOR UPDATE`).
  - DI: `IRoleRepository`, `ISessionRepository` (scoped).

- [ ] **Step 1: Viết test (đỏ)**

`Persistence/RepositoryTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class RepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public RepositoryTests(ContainersFixture containers) => _containers = containers;

    private static User NewUser() => User.Create("A", $"u-{Guid.NewGuid():N}@test.local", "hash", null);

    private async Task<(string Cs, Role Role, User Active, User Inactive, User Other)> SeedAsync()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var role = Role.Create("tester", "Tester");
        var active = NewUser();
        var inactive = NewUser();
        var other = NewUser();
        active.SetRoles([role.Id], null, Now);
        inactive.SetRoles([role.Id], null, Now);
        inactive.Deactivate();

        await using var db = TestDb.Create(cs);
        db.Roles.Add(role);
        db.Users.AddRange(active, inactive, other);
        await db.SaveChangesAsync();
        return (cs, role, active, inactive, other);
    }

    [Fact]
    public async Task CountActiveUsersInRole_IgnoresInactiveAndExcludedUser()
    {
        var (cs, role, active, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);
        var repo = new UserRepository(db);

        Assert.Equal(1, await repo.CountActiveUsersInRoleAsync(role.Id, excludingUserId: null));
        Assert.Equal(0, await repo.CountActiveUsersInRoleAsync(role.Id, excludingUserId: active.Id));
    }

    [Fact]
    public async Task GetUserIdsInRole_ReturnsAllAssignedUsers()
    {
        var (cs, role, active, inactive, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);

        var ids = await new UserRepository(db).GetUserIdsInRoleAsync(role.Id);

        Assert.Equal(new[] { active.Id, inactive.Id }.OrderBy(i => i), ids.OrderBy(i => i));
    }

    [Fact]
    public async Task GetWithAccess_LoadsRoleAssignments()
    {
        var (cs, role, active, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);

        var user = await new UserRepository(db).GetWithAccessAsync(active.Id);

        Assert.Equal(role.Id, Assert.Single(user!.RoleAssignments).RoleId);
    }

    [Fact]
    public async Task RoleRepository_FindsByCodeAndIdsAndChecksExistence()
    {
        var (cs, role, _, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);
        var repo = new RoleRepository(db);

        Assert.Equal(role.Id, (await repo.GetByCodeAsync("tester"))!.Id);
        Assert.Single(await repo.GetByIdsAsync([role.Id, Guid.NewGuid()]));
        Assert.True(await repo.CodeExistsAsync("tester"));
        Assert.False(await repo.CodeExistsAsync("nope"));
    }
}
```

`Persistence/SessionRepositoryTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class SessionRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public SessionRepositoryTests(ContainersFixture containers) => _containers = containers;

    private async Task<(string Cs, User User, SessionFamily Family)> SeedFamilyAsync(params string[] rotations)
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", $"u-{Guid.NewGuid():N}@test.local", "hash", null);
        var family = SessionFamily.Start(user.Id, "h1", Now, null, null);
        var current = "h1";
        foreach (var next in rotations)
        {
            family.Rotate(current, next, Now.AddMinutes(1));
            current = next;
        }

        await using var db = TestDb.Create(cs);
        db.Users.Add(user);
        db.SessionFamilies.Add(family);
        await db.SaveChangesAsync();
        return (cs, user, family);
    }

    [Fact]
    public async Task FindFamilyIdByTokenHash_ReturnsFamilyOrNull()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db = TestDb.Create(cs);
        var repo = new SessionRepository(db);

        Assert.Equal(family.Id, await repo.FindFamilyIdByTokenHashAsync("h1"));
        Assert.Null(await repo.FindFamilyIdByTokenHashAsync("missing"));
    }

    [Fact]
    public async Task GetForUpdate_WithoutTransaction_Throws()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db = TestDb.Create(cs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SessionRepository(db).GetForUpdateAsync(family.Id, "h1"));
    }

    [Fact]
    public async Task GetForUpdate_LoadsPresentedAndUsableTokensOnly()
    {
        var (cs, _, family) = await SeedFamilyAsync("h2", "h3");   // h1, h2 consumed; h3 usable
        await using var db = TestDb.Create(cs);
        await using var tx = await db.BeginTransactionAsync();

        var loaded = await new SessionRepository(db).GetForUpdateAsync(family.Id, "h1");

        Assert.Equal(new[] { "h1", "h3" }, loaded!.Tokens.Select(t => t.TokenHash).OrderBy(h => h));
    }

    [Fact]
    public async Task GetForUpdate_SecondTransactionWaitsAndThenSeesConsumedToken()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db1 = TestDb.Create(cs);
        await using var tx1 = await db1.BeginTransactionAsync();
        var first = await new SessionRepository(db1).GetForUpdateAsync(family.Id, "h1");

        var second = Task.Run(async () =>
        {
            await using var db2 = TestDb.Create(cs);
            await using var tx2 = await db2.BeginTransactionAsync();
            var loaded = await new SessionRepository(db2).GetForUpdateAsync(family.Id, "h1");
            var result = loaded!.Rotate("h1", "h-second", Now.AddMinutes(2));
            await db2.SaveChangesAsync();
            await tx2.CommitAsync();
            return result;
        });

        await Task.Delay(500);
        Assert.False(second.IsCompleted);   // bị chặn bởi khoá của transaction 1

        first!.Rotate("h1", "h-first", Now.AddMinutes(1));
        await db1.SaveChangesAsync();
        await tx1.CommitAsync();

        Assert.Equal(RotationResult.ReuseDetected, await second);
    }

    [Fact]
    public async Task GetActiveByUserForUpdate_ReturnsOnlyActiveFamilies()
    {
        var (cs, user, family) = await SeedFamilyAsync();
        var revoked = SessionFamily.Start(user.Id, "other-h1", Now, null, null);
        revoked.Revoke(SessionRevokeReason.Logout, Now);
        await using (var seed = TestDb.Create(cs))
        {
            seed.SessionFamilies.Add(revoked);
            await seed.SaveChangesAsync();
        }

        await using var db = TestDb.Create(cs);
        await using var tx = await db.BeginTransactionAsync();
        var families = await new SessionRepository(db).GetActiveByUserForUpdateAsync(user.Id);

        Assert.Equal(family.Id, Assert.Single(families).Id);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter "RepositoryTests|SessionRepositoryTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Hợp đồng Domain**

`IUserRepository.cs` — thêm vào interface:

```csharp
        /// Nạp user kèm RoleAssignments + PermissionGrants (tracking) để sửa quyền.
        Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default);
        Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default);
        Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default);
```

`IRoleRepository.cs`:

```csharp
namespace CleanArchCqrs.Domain.Identity;

public interface IRoleRepository
{
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
}
```

- [ ] **Step 4: Bản cài Infrastructure**

`UserRepository.cs` — thêm `using CleanArchCqrs.Domain.Identity;` (đã có) và các method:

```csharp
    public async Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default)
        => await _context.Users
            .Include(u => u.RoleAssignments)
            .Include(u => u.PermissionGrants)
            .SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default)
        => await _context.Users.CountAsync(u =>
            u.IsActive && u.Id != excludingUserId && u.RoleAssignments.Any(r => r.RoleId == roleId), ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default)
        => await _context.Set<UserRole>().Where(r => r.RoleId == roleId).Select(r => r.UserId).ToListAsync(ct);
```

`RoleRepository.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context) => _context = context;

    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Roles.Include(r => r.GrantedPermissions).SingleOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _context.Roles.Include(r => r.GrantedPermissions).SingleOrDefaultAsync(r => r.Code == code, ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await _context.Roles.Where(r => ids.Contains(r.Id)).ToListAsync(ct);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => await _context.Roles.AnyAsync(r => r.Code == code, ct);

    public async Task AddAsync(Role role, CancellationToken ct = default)
        => await _context.Roles.AddAsync(role, ct);
}
```

`SessionRepository.cs`:

```csharp
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Repositories;

/// Khoá hàng theo Đặc tả kỹ thuật §4.2: luôn family trước, token sau.
/// FOR UPDATE không ghép được với Include của EF, nên khoá bằng câu SELECT riêng rồi mới nạp;
/// ở READ COMMITTED, câu nạp sau khi có khoá luôn thấy dữ liệu đã commit mới nhất.
public sealed class SessionRepository : ISessionRepository
{
    private readonly AppDbContext _context;

    public SessionRepository(AppDbContext context) => _context = context;

    public async Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await _context.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

    public async Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default)
    {
        EnsureTransaction();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SessionFamilies\" WHERE \"Id\" = {familyId} FOR UPDATE", ct);
        if (presentedTokenHash is not null)
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"RefreshTokens\" WHERE \"FamilyId\" = {familyId} AND \"TokenHash\" = {presentedTokenHash} FOR UPDATE", ct);

        return await _context.SessionFamilies
            .Include(f => f.Tokens.Where(t => t.TokenHash == presentedTokenHash
                                           || (t.ConsumedAtUtc == null && t.RevokedAtUtc == null)))
            .SingleOrDefaultAsync(f => f.Id == familyId, ct);
    }

    public async Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default)
    {
        EnsureTransaction();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SessionFamilies\" WHERE \"UserId\" = {userId} AND \"Status\" = 'Active' ORDER BY \"Id\" FOR UPDATE", ct);

        return await _context.SessionFamilies
            .Include(f => f.Tokens.Where(t => t.ConsumedAtUtc == null && t.RevokedAtUtc == null))
            .Where(f => f.UserId == userId && f.Status == SessionStatus.Active)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SessionFamily family, CancellationToken ct = default)
        => await _context.SessionFamilies.AddAsync(family, ct);

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
    }
}
```

DI — trong `InfrastructureServiceExtensions` thêm cạnh `IUserRepository` (và `using CleanArchCqrs.Domain.Identity.Sessions;`):

```csharp
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter "RepositoryTests|SessionRepositoryTests"`
Expected: PASS 9/9 — đặc biệt `GetForUpdate_SecondTransactionWaitsAndThenSeesConsumedToken` chứng minh strict reuse dưới tranh chấp.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(persistence): add role and session repositories with FOR UPDATE row locks"
```

---

### Task 2.8: Seed danh mục quyền, 9 role, Admin đầu tiên

**Files:**
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Seed/SeedOptions.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Seed/IdentitySeeder.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/DbInitializer.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`, `appsettings.json`, `appsettings.Development.json`
- Test: `tests/CleanArchCqrs.IntegrationTests/Persistence/SeedTests.cs`

**Interfaces:**
- Consumes: `Permissions`, `SystemRoles`, `Role`, `User` (2.1–2.3), `IPasswordHasher` (đã có).
- Produces:
  - `SeedOptions { string? AdminEmail; string? AdminPassword; string AdminFullName = "Quản trị hệ thống"; }` (section `Seed`).
  - `IdentitySeeder.SeedAsync(CancellationToken ct = default)` — idempotent: đồng bộ `Permissions` (thêm/cập nhật, mã thừa chỉ log Warning), tạo role hệ thống còn thiếu, đảm bảo role `admin` có đủ `Permissions.IdentityAccess`, tạo Admin (MustChangePassword) nếu chưa ai có role `admin`.
  - `DbInitializer.InitializeAsync(IServiceProvider services, CancellationToken ct = default)` — migrate nếu `Database:MigrateOnStartup = true`, rồi seed.
  - DI: `TimeProvider.System` (singleton, `TryAdd`), `IdentitySeeder` (scoped), `SeedOptions`.

- [ ] **Step 1: Viết test (đỏ)**

`Persistence/SeedTests.cs`:

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Persistence.Seed;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class SeedTests
{
    private readonly ContainersFixture _containers;

    public SeedTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Startup_SeedsCatalogSystemRolesAndFirstAdmin()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());

        var roles = await db.Roles.Include(r => r.GrantedPermissions).ToListAsync();
        Assert.Equal(SystemRoles.All.Select(r => r.Code).OrderBy(c => c), roles.Select(r => r.Code).OrderBy(c => c));
        Assert.All(roles, r => Assert.True(r.IsSystem));

        var adminRole = roles.Single(r => r.Code == SystemRoles.Admin);
        Assert.Equal(
            Permissions.IdentityAccess.Select(p => p.Code).OrderBy(c => c),
            adminRole.GrantedPermissions.Select(p => p.PermissionCode).OrderBy(c => c));

        var admin = await db.Users.Include(u => u.RoleAssignments).SingleAsync(u => u.Email == factory.AdminEmail);
        Assert.True(admin.MustChangePassword);
        Assert.True(admin.HasRole(adminRole.Id));
    }

    [Fact]
    public async Task Seeder_RunTwice_IsIdempotent()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();

        Assert.Equal(SystemRoles.All.Count, await db.Roles.CountAsync());
        Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter SeedTests`
Expected: FAIL biên dịch (`IdentitySeeder` chưa có).

- [ ] **Step 3: Seeder và initializer**

`Seed/SeedOptions.cs`:

```csharp
namespace CleanArchCqrs.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public string? AdminEmail { get; set; }
    /// Mật khẩu tạm — Admin bị bắt đổi ở lần đăng nhập đầu. Dev: user-secrets; prod: biến môi trường.
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "Quản trị hệ thống";
}
```

`Seed/IdentitySeeder.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Persistence.Seed;

public sealed class IdentitySeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SeedOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(AppDbContext db, IPasswordHasher passwordHasher, IOptions<SeedOptions> options,
        TimeProvider time, ILogger<IdentitySeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SyncPermissionCatalogAsync(ct);
        var adminRole = await EnsureSystemRolesAsync(ct);
        await EnsureFirstAdminAsync(adminRole, ct);
    }

    private async Task SyncPermissionCatalogAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Id, ct);
        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Code, out var permission)) permission.Update(definition);
            else _db.Permissions.Add(Permission.Create(definition));
        }

        foreach (var obsolete in existing.Keys.Where(code => !Permissions.IsDefined(code)))
            _logger.LogWarning("Permission {PermissionCode} exists in the database but not in the code catalog", obsolete);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Role> EnsureSystemRolesAsync(CancellationToken ct)
    {
        var roles = await _db.Roles.Include(r => r.GrantedPermissions).ToDictionaryAsync(r => r.Code, ct);
        foreach (var (code, name) in SystemRoles.All)
        {
            if (roles.ContainsKey(code)) continue;
            var role = Role.Create(code, name, isSystem: true);
            _db.Roles.Add(role);
            roles[code] = role;
        }

        var admin = roles[SystemRoles.Admin];
        admin.SetPermissions(admin.GrantedPermissions.Select(p => p.PermissionCode)
            .Union(Permissions.IdentityAccess.Select(p => p.Code)));

        await _db.SaveChangesAsync(ct);
        return admin;
    }

    private async Task EnsureFirstAdminAsync(Role adminRole, CancellationToken ct)
    {
        if (await _db.Set<UserRole>().AnyAsync(r => r.RoleId == adminRole.Id, ct)) return;

        if (string.IsNullOrWhiteSpace(_options.AdminEmail) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning("No admin user exists and Seed:AdminEmail / Seed:AdminPassword are not configured");
            return;
        }

        var admin = User.Create(_options.AdminFullName, _options.AdminEmail, _passwordHasher.Hash(_options.AdminPassword), null);
        admin.SetRoles([adminRole.Id], assignedBy: null, _time.GetUtcNow());
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded first admin user {UserId}", admin.Id);
    }
}
```

`Persistence/DbInitializer.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.Infrastructure.Persistence;

public static class DbInitializer
{
    /// Production: để MigrateOnStartup = false và chạy migration như một bước deploy riêng.
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(ct);

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(ct);
    }
}
```

DI — trong `AddInfrastructureServices` thêm (và `using Microsoft.Extensions.DependencyInjection.Extensions;`, `using CleanArchCqrs.Infrastructure.Persistence.Seed;`):

```csharp
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<SeedOptions>(configuration.GetSection("Seed"));
        services.AddScoped<IdentitySeeder>();
```

`Program.cs` — ngay sau `var app = builder.Build();` thêm (và `using CleanArchCqrs.Infrastructure.Persistence;`):

```csharp
        await DbInitializer.InitializeAsync(app.Services);
```

`appsettings.json` thêm ở gốc:

```json
  "Database": { "MigrateOnStartup": false },
  "Seed": { "AdminEmail": "", "AdminPassword": "", "AdminFullName": "Quản trị hệ thống" },
```

`appsettings.Development.json` thêm:

```json
  "Database": { "MigrateOnStartup": true },
  "Seed": { "AdminEmail": "admin@hospital.local" }
```

Đặt mật khẩu tạm dev bằng user-secrets (không commit):

```bash
dotnet user-secrets init --project src/CleanArchCqrs.API
dotnet user-secrets set "Seed:AdminPassword" "Doi-Mat-Khau-Ngay-1" --project src/CleanArchCqrs.API
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter SeedTests`
Expected: PASS 2/2.

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests`
Expected: toàn bộ xanh (test `/health` giờ cũng chạy migrate + seed khi khởi động).

- [ ] **Step 5: Kiểm tra tay trên DB dev**

Run: `docker compose up -d postgres && dotnet run --project src/CleanArchCqrs.API` rồi dừng app, sau đó
`docker compose exec postgres psql -U postgres -d CleanArchCqrsDb -c 'SELECT "Code","Name" FROM "Roles" ORDER BY "Code";'`
Expected: 9 dòng role; bảng `Users` có `admin@hospital.local` với `MustChangePassword = t`.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(identity): seed permission catalog, nine system roles and first admin on startup"
```
