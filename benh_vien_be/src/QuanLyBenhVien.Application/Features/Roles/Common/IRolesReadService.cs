namespace QuanLyBenhVien.Application.Features.Roles.Common;

public interface IRolesReadService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct);
}
