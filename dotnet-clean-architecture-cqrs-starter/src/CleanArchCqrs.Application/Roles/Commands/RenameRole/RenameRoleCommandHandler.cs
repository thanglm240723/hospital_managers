using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.RenameRole;

public sealed class RenameRoleCommandHandler : IRequestHandler<RenameRoleCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RenameRoleCommandHandler(IRoleRepository roles, IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RenameRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct)
                   ?? throw new NotFoundException($"Role '{request.RoleId}' was not found.");
        role.Rename(request.Name);   // Code không đổi được — kể cả role hệ thống vẫn đổi được tên hiển thị
        _audit.Record(AuditActions.RoleUpdate, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
