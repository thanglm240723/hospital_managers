using MediatR;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.GetStaffProfile;

internal sealed class GetStaffProfileQueryHandler(IStaffProfilesReadService readService)
    : IRequestHandler<GetStaffProfileQuery, Result<StaffProfileDto>>
{
    public async Task<Result<StaffProfileDto>> Handle(GetStaffProfileQuery request, CancellationToken cancellationToken)
    {
        var dto = await readService.GetByUserIdAsync(request.UserId, cancellationToken);
        return dto is null ? StaffProfilesErrors.NotFound : dto;
    }
}
