using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Application.Features.Facilities.GetFacilityTree;

internal sealed class GetFacilityTreeQueryHandler(IFacilitiesReadService readService)
    : IRequestHandler<GetFacilityTreeQuery, Result<IReadOnlyList<BranchDto>>>
{
    public async Task<Result<IReadOnlyList<BranchDto>>> Handle(GetFacilityTreeQuery request, CancellationToken cancellationToken)
        => Result.Success<IReadOnlyList<BranchDto>>(await readService.GetTreeAsync(cancellationToken));
}
