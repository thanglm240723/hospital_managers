namespace QuanLyBenhVien.Application.Features.Facilities.Common;

public interface IFacilitiesReadService
{
    Task<IReadOnlyList<BranchDto>> GetTreeAsync(CancellationToken ct);
}
