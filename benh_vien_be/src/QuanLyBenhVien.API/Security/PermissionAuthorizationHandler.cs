using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Presentation.Auth;

namespace QuanLyBenhVien.API.Security;

/// Singleton nên lấy IPermissionService (scoped) từ RequestServices của request.
/// Lỗi đọc quyền ném exception ⇒ request không đi tiếp.
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string PasswordChangeRequiredReason = "password_change_required";
    public const string UnauthenticatedReason = "unauthenticated";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.Resource is not HttpContext http
            || !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            context.Fail(new AuthorizationFailureReason(this, UnauthenticatedReason));
            return;
        }

        var access = await http.RequestServices.GetRequiredService<IPermissionService>().GetAsync(userId, http.RequestAborted);
        // Tài khoản không tồn tại hoặc đã khóa: coi như chưa xác thực (401), không phải thiếu quyền (403 + audit).
        if (access is null || !access.IsActive)
        {
            context.Fail(new AuthorizationFailureReason(this, UnauthenticatedReason));
            return;
        }

        if (access.MustChangePassword
            && http.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredMetadata>() is null)
        {
            context.Fail(new AuthorizationFailureReason(this, PasswordChangeRequiredReason));
            return;
        }

        if (access.Permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
