using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;

namespace QuanLyBenhVien.Application.Features.Facilities.UpdateDepartment;

internal sealed class UpdateDepartmentCommandHandler(
    IFacilityRepository facilities,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<UpdateDepartmentCommand, Result<DepartmentDto>>
{
    public async Task<Result<DepartmentDto>> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var entity = await facilities.GetDepartmentForUpdateAsync(request.Id, cancellationToken);
        if (entity is null) return FacilitiesErrors.NotFound;
        if (entity.RowVersion != request.ExpectedVersion) return FacilitiesErrors.VersionConflict;
        if (entity.IsActive && !request.IsActive && await facilities.HasActiveRoomsAsync(entity.Id, cancellationToken))
            return FacilitiesErrors.HasActiveChildren;

        entity.Rename(request.Name);
        entity.SetActive(request.IsActive);
        auditWriter.Record(AuditActions.FacilityUpdate, AuditResult.Succeeded,
            resourceType: "Department", resourceId: entity.Id.ToString(), actorId: currentUser.UserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return FacilitiesErrors.ToDto(entity);
    }
}
