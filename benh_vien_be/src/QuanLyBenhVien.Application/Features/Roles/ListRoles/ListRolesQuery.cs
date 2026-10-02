using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.ListRoles;

public sealed record ListRolesQuery() : IQuery<Result<IReadOnlyList<RoleDto>>>;
