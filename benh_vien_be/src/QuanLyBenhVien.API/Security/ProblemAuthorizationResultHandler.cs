using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Presentation.Security;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.API.Security;

/// Map kết quả authorization sang Problem Details (401 `unauthenticated`, 403 `forbidden` / `password_change_required`).
/// Từ chối do thiếu quyền được ghi audit bền vững TRƯỚC khi trả 403; ghi lỗi ⇒ exception, request không chạy (fail closed).
/// Audit chỉ có actor, quyền, method, path (không query/body/token).
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await Write(context, StatusCodes.Status401Unauthorized, "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
            return;
        }

        // Chỉ tin lý do do chính PermissionAuthorizationHandler phát ra, không so khớp chuỗi message của handler khác.
        var reasons = authorizeResult.AuthorizationFailure?.FailureReasons
            .Where(r => r.Handler is PermissionAuthorizationHandler)
            .Select(r => r.Message).ToList() ?? [];
        if (reasons.Contains(PermissionAuthorizationHandler.UnauthenticatedReason))
        {
            await Write(context, StatusCodes.Status401Unauthorized, "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
            return;
        }

        if (reasons.Contains(PermissionAuthorizationHandler.PasswordChangeRequiredReason))
        {
            await Write(context, StatusCodes.Status403Forbidden, "password_change_required", "Bạn cần đổi mật khẩu trước khi tiếp tục.");
            return;
        }

        var permission = policy.Requirements.OfType<PermissionRequirement>().FirstOrDefault()?.Permission;
        if (permission is not null)
            await AuditDenialAsync(context, permission);

        await Write(context, StatusCodes.Status403Forbidden, "forbidden", "Không đủ quyền.");
    }

    private static async Task AuditDenialAsync(HttpContext context, string permission)
    {
        var services = context.RequestServices;
        Guid? actorId = Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : null;
        services.GetRequiredService<IAuditWriter>().Record(
            AuditActions.AuthorizationDenied,
            AuditResult.Denied,
            reason: "missing_permission",
            actorId: actorId,
            metadata: new Dictionary<string, object?>
            {
                ["permission"] = permission,
                ["method"] = context.Request.Method,
                ["path"] = context.Request.Path.Value,
            });
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(context.RequestAborted);
    }

    private static Task Write(HttpContext context, int status, string code, string title)
        => ProblemResponses.Create(context, status, code, title).ExecuteAsync(context);
}
