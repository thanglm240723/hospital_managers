using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.ActivateUser;

public sealed class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand>
{
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ActivateUserCommandHandler(IUserRepository users, ICacheInvalidator cacheInvalidator, IAuditRecorder audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ActivateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetUserByIdAsync(request.UserId, ct);   // NotFoundException ⇒ 404
        if (user.IsActive) return;

        user.Activate();
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserActivate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
