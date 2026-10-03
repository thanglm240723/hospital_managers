using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Permissions.Common;

namespace QuanLyBenhVien.Application.Features.Permissions.ListPermissions;

internal sealed class ListPermissionsQueryHandler(IPermissionsReadService read)
    : IRequestHandler<ListPermissionsQuery, Result<IReadOnlyList<PermissionDto>>>
{
    public async Task<Result<IReadOnlyList<PermissionDto>>> Handle(ListPermissionsQuery request, CancellationToken cancellationToken)
        => Result.Success(await read.ListAsync(cancellationToken));
}
