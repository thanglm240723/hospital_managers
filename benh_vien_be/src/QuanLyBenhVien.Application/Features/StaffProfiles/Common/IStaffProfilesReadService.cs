namespace QuanLyBenhVien.Application.Features.StaffProfiles.Common;

public interface IStaffProfilesReadService
{
    Task<StaffProfileDto?> GetByUserIdAsync(Guid userId, CancellationToken ct);
}
