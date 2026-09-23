using Serilog.Context;

namespace CleanArchCqrs.API.Middleware;

/// Gắn UserId/SessionFamilyId vào mọi dòng log trong request đã xác thực. Đặt SAU UseAuthentication.
public sealed class UserLogContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserLogContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        using (LogContext.PushProperty("UserId", context.User.FindFirst("sub")?.Value))
        using (LogContext.PushProperty("SessionFamilyId", context.User.FindFirst("fid")?.Value))
        {
            await _next(context);
        }
    }
}
