using MediatR;
using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.ListUsers;

internal sealed class ListUsersQueryHandler(IUsersReadService read)
    : IRequestHandler<ListUsersQuery, Result<PagedResult<UserSummaryDto>>>
{
    public async Task<Result<PagedResult<UserSummaryDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
        => Result.Success(await read.ListAsync(request, cancellationToken));
}
