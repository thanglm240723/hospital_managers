using QuanLyBenhVien.Application.Features.Permissions.ListPermissions;

namespace QuanLyBenhVien.Application.Features.Permissions.Common;

public interface IPermissionsReadService
{
    Task<IReadOnlyList<PermissionDto>> ListAsync(CancellationToken ct);
}
