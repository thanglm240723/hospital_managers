using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public CreateUserCommandHandler(IUserRepository users, IRoleRepository roles, IPasswordHasher passwordHasher,
        ICurrentUser currentUser, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _users = users;
        _roles = roles;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        if (await _users.EmailExistsAsync(request.Email, ct))
            throw new ConflictException(ErrorCodes.EmailTaken, "Email đã được dùng cho tài khoản khác.");

        var roleIds = request.RoleIds.Distinct().ToList();
        if ((await _roles.GetByIdsAsync(roleIds, ct)).Count != roleIds.Count)
            throw new ValidationException(nameof(request.RoleIds), "Có vai trò không tồn tại.");

        var user = User.Create(request.FullName, request.Email, _passwordHasher.Hash(request.TemporaryPassword), avatarUrl: null);
        user.SetRoles(roleIds, _currentUser.UserId, _time.GetUtcNow());
        await _users.AddUserAsync(user, ct);
        _audit.Record(AuditActions.UserCreate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["roleIds"] = roleIds });
        await _unitOfWork.SaveChangesAsync(ct);
        return user.Id;
    }
}
