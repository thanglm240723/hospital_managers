namespace CleanArchCqrs.Application.Common.Auditing;

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string RefreshReuse = "auth.refresh.reuse";
    public const string Logout = "auth.logout";
    public const string LogoutAll = "auth.logout_all";
    public const string SessionRevoke = "auth.session.revoke";
    public const string PasswordChange = "auth.password.change";
    public const string RateLimited = "auth.rate_limited";
    public const string AuthorizationDenied = "authz.denied";
    public const string UserCreate = "users.create";
    public const string UserActivate = "users.activate";
    public const string UserDeactivate = "users.deactivate";
    public const string UserRolesSet = "users.roles.set";
    public const string UserPermissionGrant = "users.permissions.grant";
    public const string UserPermissionRevoke = "users.permissions.revoke";
    public const string RoleCreate = "roles.create";
    public const string RoleUpdate = "roles.update";
    public const string RolePermissionsSet = "roles.permissions.set";
}
