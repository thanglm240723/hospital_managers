namespace QuanLyBenhVien.Application.Common.Auditing;

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string PasswordChange = "auth.password.change";
    public const string Logout = "auth.logout";
    public const string LogoutAll = "auth.logout_all";
    public const string RefreshReuse = "auth.refresh.reuse";
    public const string RoleCreate = "roles.create";
    public const string RoleRename = "roles.rename";
    public const string RoleSetPermissions = "roles.set_permissions";
    public const string UserCreate = "users.create";
    public const string UserActivate = "users.activate";
    public const string UserDeactivate = "users.deactivate";
    public const string UserSetRoles = "users.set_roles";
    public const string UserPermissionGrant = "users.permissions.grant";
    public const string UserPermissionRevoke = "users.permissions.revoke";
    public const string FacilityCreate = "facilities.create";
    public const string FacilityUpdate = "facilities.update";
    public const string StaffProfileCreate = "staff_profiles.create";
    public const string StaffProfileUpdate = "staff_profiles.update";
    public const string StaffWorkScopesSet = "staff_profiles.set_work_scopes";
    public const string AuthorizationDenied = "auth.authorization.denied";
    public const string ResourceAccessDenied = "auth.resource.denied";
}
