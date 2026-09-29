using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity.Events;

namespace QuanLyBenhVien.Domain.Identity;

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

    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("New password hash cannot be empty.", nameof(newPasswordHash));
        if (newPasswordHash == PasswordHash)
            throw new InvalidOperationException("New password hash cannot be the same as the current password hash.");

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        SecurityVersion++;
        UpdatedAt = now;
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

    public void RecordLogin(DateTimeOffset now)
    {
        LastLoginAt = now;
        UpdatedAt = now;
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
