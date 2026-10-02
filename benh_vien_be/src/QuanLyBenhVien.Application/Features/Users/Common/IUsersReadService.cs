using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Application.Features.Users.ListUsers;

namespace QuanLyBenhVien.Application.Features.Users.Common;

public interface IUsersReadService
{
    Task<PagedResult<UserSummaryDto>> ListAsync(ListUsersQuery query, CancellationToken ct);

    Task<UserDetailDto?> GetAsync(Guid id, CancellationToken ct);
}
