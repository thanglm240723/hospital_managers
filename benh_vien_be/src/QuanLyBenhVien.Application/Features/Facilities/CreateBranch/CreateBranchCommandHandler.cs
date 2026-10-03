using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateBranch;

internal sealed class CreateBranchCommandHandler(
    IFacilityRepository facilities,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<CreateBranchCommand, Result<BranchDto>>
{
    public async Task<Result<BranchDto>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = Branch.Create(request.Code, request.Name);
        await facilities.AddAsync(branch, cancellationToken);
        auditWriter.Record(AuditActions.FacilityCreate, AuditResult.Succeeded,
            resourceType: "Branch", resourceId: branch.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == FacilitiesErrors.BranchCodeConstraint)
        {
            return FacilitiesErrors.CodeTaken;
        }

        return FacilitiesErrors.ToDto(branch);
    }
}
