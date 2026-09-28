using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace QuanLyBenhVien.Presentation.Http;

/// `/internal/*` chỉ Gateway gọi. Gateway không lộ route `/internal/*` ra ngoài; filter này là lớp phòng thủ thứ hai.
public sealed class InternalApiKeyFilter(IOptions<InternalApiOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Internal-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.Key));
        var provided = SHA256.HashData(Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString()));

        if (string.IsNullOrEmpty(options.Value.Key) || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return ProblemResponses.Create(context.HttpContext, StatusCodes.Status401Unauthorized,
                "unauthenticated", "Thiếu hoặc sai khoá nội bộ.");
        }

        return await next(context);
    }
}
