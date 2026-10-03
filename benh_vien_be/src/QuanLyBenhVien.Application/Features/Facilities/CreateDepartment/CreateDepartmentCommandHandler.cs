using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;

internal sealed class CreateDepartmentCommandHandler(
    IFacilityRepository facilities,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<CreateDepartmentCommand, Result<DepartmentDto>>
{
    public async Task<Result<DepartmentDto>> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var branch = await facilities.GetBranchForUpdateAsync(request.BranchId, cancellationToken);
        if (branch is null) return FacilitiesErrors.NotFound;
        if (!branch.IsActive) return FacilitiesErrors.ParentInactive;

        FacilitiesErrors.TryParseKind(request.Kind, out var kind);
        var department = Department.Create(branch.Id, request.Code, request.Name, kind);
        await facilities.AddAsync(department, cancellationToken);
        auditWriter.Record(AuditActions.FacilityCreate, AuditResult.Succeeded,
            resourceType: "Department", resourceId: department.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == FacilitiesErrors.DepartmentCodeConstraint)
        {
            return FacilitiesErrors.CodeTaken;
        }

        await transaction.CommitAsync(cancellationToken);
        return FacilitiesErrors.ToDto(department);
    }
}
