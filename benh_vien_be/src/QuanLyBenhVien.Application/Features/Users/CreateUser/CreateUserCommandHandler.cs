using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Users.CreateUser;

internal sealed class CreateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IUsersReadService readService,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IInitialPasswordGenerator passwordGenerator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    TimeProvider time)
    : IRequestHandler<CreateUserCommand, Result<CreateUserResultDto>>
{
    private const int MaxGenerateAttempts = 20;

    public async Task<Result<CreateUserResultDto>> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return UsersErrors.EmailTaken;
        }

        if (request.RoleIds.Count > 0 && (await roles.GetByIdsAsync(request.RoleIds, cancellationToken)).Count != request.RoleIds.Count)
        {
            return UsersErrors.UnknownRoles;
        }

        var password = GeneratePassword(email);
        var now = time.GetUtcNow();
        var user = User.Create(request.FullName, email, passwordHasher.Hash(password), avatarUrl: null, createdAt: now);
        user.SetRoles(request.RoleIds.ToList(), currentUser.UserId, now);
        await users.AddUserAsync(user, cancellationToken);
        auditWriter.Record(AuditActions.UserCreate, AuditResult.Succeeded,
            resourceType: "User", resourceId: user.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            // User mới chưa có cache quyền ⇒ không cần invalidation; một lần SaveChanges (EF tự bọc transaction).
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UsersErrors.EmailUniqueConstraint)
        {
            return UsersErrors.EmailTaken;
        }

        var dto = await readService.GetAsync(user.Id, cancellationToken);
        return dto is null ? UsersErrors.NotFound : new CreateUserResultDto(dto, password);
    }

    /// Mật khẩu phải thoả PasswordPolicy (độ dài, không chứa local-part email); vi phạm thì sinh lại.
    private string GeneratePassword(string email)
    {
        for (var i = 0; i < MaxGenerateAttempts; i++)
        {
            var candidate = passwordGenerator.Generate();
            if (candidate.Length is >= PasswordPolicy.MinLength and <= PasswordPolicy.MaxLength
                && !PasswordPolicy.ContainsEmailLocalPart(candidate, email))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không sinh được mật khẩu ban đầu thoả chính sách mật khẩu.");
    }
}
