using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Facilities.Common;
using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;

namespace QuanLyBenhVien.Application.Features.Facilities.CreateRoom;

internal sealed class CreateRoomCommandHandler(
    IFacilityRepository facilities,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<CreateRoomCommand, Result<RoomDto>>
{
    public async Task<Result<RoomDto>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var department = await facilities.GetDepartmentForUpdateAsync(request.DepartmentId, cancellationToken);
        if (department is null) return FacilitiesErrors.NotFound;
        if (!department.IsActive) return FacilitiesErrors.ParentInactive;

        var room = Room.Create(department.Id, request.Code, request.Name);
        await facilities.AddAsync(room, cancellationToken);
        auditWriter.Record(AuditActions.FacilityCreate, AuditResult.Succeeded,
            resourceType: "Room", resourceId: room.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == FacilitiesErrors.RoomCodeConstraint)
        {
            return FacilitiesErrors.CodeTaken;
        }

        await transaction.CommitAsync(cancellationToken);
        return FacilitiesErrors.ToDto(room);
    }
}
