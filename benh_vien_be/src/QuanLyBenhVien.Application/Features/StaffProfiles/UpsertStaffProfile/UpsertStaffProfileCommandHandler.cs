using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Staff;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.UpsertStaffProfile;

internal sealed class UpsertStaffProfileCommandHandler(
    IStaffProfileRepository profiles,
    IUserRepository users,
    IStaffProfilesReadService readService,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<UpsertStaffProfileCommand, Result<StaffProfileDto>>
{
    public async Task<Result<StaffProfileDto>> Handle(UpsertStaffProfileCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Khóa hàng user để tuần tự hóa tạo/sửa hồ sơ của cùng một người.
        var user = await users.GetForUpdateAsync(request.UserId, cancellationToken);
        if (user is null) return StaffProfilesErrors.UserNotFound;

        var profile = await profiles.GetByUserIdAsync(request.UserId, cancellationToken);
        string action;
        if (request.ExpectedVersion is null)
        {
            if (profile is not null) return StaffProfilesErrors.AlreadyExists;
            profile = StaffProfile.Create(request.UserId, request.StaffCode);
            profile.SetActive(request.IsActive);
            await profiles.AddAsync(profile, cancellationToken);
            action = AuditActions.StaffProfileCreate;
        }
        else
        {
            if (profile is null) return StaffProfilesErrors.NotFound;
            if (profile.RowVersion != request.ExpectedVersion) return StaffProfilesErrors.VersionConflict;
            profile.ChangeStaffCode(request.StaffCode);
            profile.SetActive(request.IsActive);
            action = AuditActions.StaffProfileUpdate;
        }

        auditWriter.Record(action, AuditResult.Succeeded,
            resourceType: "StaffProfile", resourceId: profile.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == StaffProfilesErrors.StaffCodeConstraint)
        {
            return StaffProfilesErrors.CodeTaken;
        }
        catch (ConcurrencyConflictException)
        {
            return StaffProfilesErrors.VersionConflict;
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == StaffProfilesErrors.UserIdConstraint)
        {
            return StaffProfilesErrors.AlreadyExists;
        }

        await transaction.CommitAsync(cancellationToken);
        var dto = await readService.GetByUserIdAsync(request.UserId, cancellationToken);
        return dto is null ? StaffProfilesErrors.NotFound : dto;
    }
}
