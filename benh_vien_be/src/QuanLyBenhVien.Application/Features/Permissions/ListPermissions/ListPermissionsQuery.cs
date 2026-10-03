using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Permissions.ListPermissions;

public sealed record ListPermissionsQuery() : IQuery<Result<IReadOnlyList<PermissionDto>>>;
