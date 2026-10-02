using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Users.ActivateUser;

/// Đặt trạng thái đích "đang hoạt động". Không hồi sinh session family đã thu hồi — user phải đăng nhập lại.
internal sealed class ActivateUserCommandHandler(
    IUserRepository users,
    IUsersReadService readService,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<ActivateUserCommandHandler> logger)
    : IRequestHandler<ActivateUserCommand, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(ActivateUserCommand request, CancellationToken cancellationToken)
    {
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var user = await users.GetForUpdateAsync(request.Id, cancellationToken);
            if (user is null)
            {
                return UsersErrors.NotFound;
            }

            if (!user.IsActive)
            {
                user.Activate(time.GetUtcNow());
                cacheInvalidator.InvalidatePermissions(user.Id);
            }

            auditWriter.Record(AuditActions.UserActivate, AuditResult.Succeeded,
                resourceType: "User", resourceId: user.Id.ToString(), actorId: currentUser.UserId);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await UserCommandCompletion.FlushAndReadAsync(cacheInvalidator, readService, logger, request.Id, cancellationToken);
    }
}
