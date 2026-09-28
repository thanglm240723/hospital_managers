using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace QuanLyBenhVien.API.Middleware;

/// Gắn UserId/SessionFamilyId vào mọi dòng log trong request đã xác thực. Đặt SAU UseAuthentication.
public sealed class UserLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        using (LogContext.PushProperty("UserId", context.User.FindFirst("sub")?.Value))
        using (LogContext.PushProperty("SessionFamilyId", context.User.FindFirst("fid")?.Value))
        {
            await next(context);
        }
    }
}
