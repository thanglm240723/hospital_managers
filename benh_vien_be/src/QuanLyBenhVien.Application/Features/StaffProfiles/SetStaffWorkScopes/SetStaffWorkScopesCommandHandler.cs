using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Staff;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.SetStaffWorkScopes;

internal sealed class SetStaffWorkScopesCommandHandler(
    IStaffProfileRepository profiles,
    IUserRepository users,
    IFacilityRepository facilities,
    IStaffProfilesReadService readService,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<SetStaffWorkScopesCommand, Result<StaffProfileDto>>
{
    public async Task<Result<StaffProfileDto>> Handle(SetStaffWorkScopesCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Khóa hàng user trước khi đọc/so version để các PUT đồng thời được tuần tự hóa.
        var user = await users.GetForUpdateAsync(request.UserId, cancellationToken);
        if (user is null) return StaffProfilesErrors.UserNotFound;

        var profile = await profiles.GetByUserIdAsync(request.UserId, cancellationToken);
        if (profile is null) return StaffProfilesErrors.NotFound;
        if (profile.RowVersion != request.ExpectedVersion) return StaffProfilesErrors.VersionConflict;

        var ids = request.DepartmentIds.Distinct().ToList();
        var departments = await facilities.GetDepartmentsAsync(ids, cancellationToken);
        var valid = departments.Where(d => d.IsActive).ToDictionary(d => d.Id);
        var invalid = ids.Where(id => !valid.ContainsKey(id)).ToList();
        if (invalid.Count > 0) return StaffProfilesErrors.InvalidDepartments(invalid);

        // BranchId lấy từ khoa trong DB, không tin client.
        if (profile.SetWorkScopes(ids.Select(id => (id, valid[id].BranchId))))
        {
            profiles.MarkChanged(profile);
            auditWriter.Record(AuditActions.StaffWorkScopesSet, AuditResult.Succeeded,
                resourceType: "StaffProfile", resourceId: profile.Id.ToString(), actorId: currentUser.UserId,
                metadata: new Dictionary<string, object?> { ["DepartmentIds"] = ids.Select(i => i.ToString()).ToArray() });
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (QuanLyBenhVien.Domain.Exceptions.ConcurrencyConflictException)
            {
                return StaffProfilesErrors.VersionConflict;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var dto = await readService.GetByUserIdAsync(request.UserId, cancellationToken);
        return dto is null ? StaffProfilesErrors.NotFound : dto;
    }
}
