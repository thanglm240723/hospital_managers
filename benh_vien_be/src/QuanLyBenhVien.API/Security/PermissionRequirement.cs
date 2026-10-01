using Microsoft.AspNetCore.Authorization;

namespace QuanLyBenhVien.API.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
