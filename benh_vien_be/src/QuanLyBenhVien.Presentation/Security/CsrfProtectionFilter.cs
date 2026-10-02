using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.Presentation.Security;

/// CSRF cho route đổi trạng thái phiên (spec V2 §5.3): Origin thuộc allowlist và X-CSRF-Token = HMAC(family).
/// Family lấy theo `CsrfProtectionMetadata.Source`: `Bearer` từ claim `fid` (route cần đăng nhập) hoặc
/// `RefreshCookie` từ cookie `__Host-rt` qua `IRefreshSessionLookup` (route công khai như logout).
/// Cookie thiếu/không nhận diện được family ⇒ không có gì để bảo vệ, filter để handler tự quyết (không mutate).
/// Family nhận diện được nhưng Origin/CSRF sai ⇒ 403 `csrf_failed`, handler không chạy.
public sealed class CsrfProtectionFilter(
    ICsrfTokenService csrf,
    IRequestOriginPolicy originPolicy,
    IRefreshTokenGenerator refreshTokens,
    IRefreshSessionLookup sessionLookup) : IEndpointFilter
{
    public const string HeaderName = "X-CSRF-Token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var metadata = http.GetEndpoint()?.Metadata.GetMetadata<CsrfProtectionMetadata>() ?? CsrfProtectionMetadata.Bearer;

        Guid? familyId = metadata.Source == CsrfTokenSource.RefreshCookie
            ? await ResolveCookieFamilyAsync(http)
            : ResolveBearerFamily(http);

        if (familyId is null)
        {
            if (metadata.Source == CsrfTokenSource.RefreshCookie)
            {
                return await next(context);
            }

            return Forbidden(http);
        }

        var origin = http.Request.Headers.Origin.ToString();
        var token = http.Request.Headers[HeaderName].ToString();
        if (!originPolicy.IsAllowed(origin) || !csrf.IsValid(familyId.Value, token))
        {
            return Forbidden(http);
        }

        return await next(context);
    }

    private static Guid? ResolveBearerFamily(HttpContext http)
        => Guid.TryParse(http.User.FindFirst("fid")?.Value, out var familyId) ? familyId : null;

    private async Task<Guid?> ResolveCookieFamilyAsync(HttpContext http)
    {
        var cookie = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        var session = await sessionLookup.FindAsync(refreshTokens.Hash(cookie), http.RequestAborted);
        return session?.FamilyId;
    }

    private static IResult Forbidden(HttpContext http) =>
        ProblemResponses.Create(http, StatusCodes.Status403Forbidden, "csrf_failed",
            "Yêu cầu không hợp lệ (CSRF).");
}
