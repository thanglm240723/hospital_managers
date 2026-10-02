using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Presentation.Security;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.API.Security;

/// Bắt đổi mật khẩu lần đầu (spec cũ §3.9, spec V2 §5.3). Đặt SAU UseAuthentication/UseAuthorization, trước endpoint.
/// User đã xác thực có MustChangePassword ⇒ 403 `password_change_required`, trừ route gắn
/// AllowWhilePasswordChangeRequiredMetadata. Route anonymous (login/refresh/internal) theo cơ chế riêng.
/// Đọc cờ lỗi ⇒ exception lan ra exception handler, request không đi tiếp.
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IPermissionService permissions)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null
            || context.User.Identity?.IsAuthenticated != true
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredMetadata>() is not null)
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            await Write(context, StatusCodes.Status401Unauthorized, "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
            return;
        }

        var access = await permissions.GetAsync(userId, context.RequestAborted);
        if (access is null)
        {
            await Write(context, StatusCodes.Status401Unauthorized, "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
            return;
        }

        if (access.MustChangePassword)
        {
            await Write(context, StatusCodes.Status403Forbidden, "password_change_required", "Bạn cần đổi mật khẩu trước khi tiếp tục.");
            return;
        }

        await next(context);
    }

    private static Task Write(HttpContext context, int status, string code, string title)
        => ProblemResponses.Create(context, status, code, title).ExecuteAsync(context);
}
