using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CleanArchCqrs.API.Authorization;

/// Viết 403 dạng Problem Details và ghi audit authz.denied. 401 để JwtBearer OnChallenge xử lý.
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Message == ErrorCodes.PasswordChangeRequired) == true)
        {
            await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired,
                "Bạn cần đổi mật khẩu trước khi tiếp tục.");
            return;
        }

        // Chưa có transaction nghiệp vụ nào ở bước authorize — lưu audit trong scope request là đủ.
        var required = policy.Requirements.OfType<PermissionRequirement>().Select(r => r.Permission).ToArray();
        context.RequestServices.GetRequiredService<IAuditRecorder>().Record(
            AuditActions.AuthorizationDenied, AuditResult.Denied, reason: "MissingPermission",
            metadata: new Dictionary<string, object?> { ["permissions"] = required, ["path"] = context.Request.Path.Value });
        await context.RequestServices.GetRequiredService<IUnitOfWork>().SaveChangesAsync(context.RequestAborted);

        await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
            "Bạn không có quyền thực hiện thao tác này.");
    }
}
