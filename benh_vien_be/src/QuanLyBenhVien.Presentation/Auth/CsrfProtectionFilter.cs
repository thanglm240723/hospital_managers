using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.Presentation.Auth;

/// CSRF cho endpoint cần access token (spec cũ §3.1): Origin thuộc allowlist và X-CSRF-Token = HMAC(fid từ Bearer).
/// Sai bất kỳ ⇒ 403 `csrf_failed`, handler không chạy.
public sealed class CsrfProtectionFilter(ICsrfTokenService csrf, IRequestOriginPolicy originPolicy) : IEndpointFilter
{
    public const string HeaderName = "X-CSRF-Token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var origin = http.Request.Headers.Origin.ToString();
        var token = http.Request.Headers[HeaderName].ToString();

        var valid = originPolicy.IsAllowed(origin)
                    && Guid.TryParse(http.User.FindFirst("fid")?.Value, out var familyId)
                    && csrf.IsValid(familyId, token);

        if (!valid)
        {
            return ProblemResponses.Create(http, StatusCodes.Status403Forbidden, "csrf_failed",
                "Yêu cầu không hợp lệ (CSRF).");
        }

        return await next(context);
    }
}
