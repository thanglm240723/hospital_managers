using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.ListRoles;

internal sealed class ListRolesQueryHandler(IRolesReadService read)
    : IRequestHandler<ListRolesQuery, Result<IReadOnlyList<RoleDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListRolesQuery request, CancellationToken cancellationToken)
        => Result.Success(await read.ListAsync(cancellationToken));
}
