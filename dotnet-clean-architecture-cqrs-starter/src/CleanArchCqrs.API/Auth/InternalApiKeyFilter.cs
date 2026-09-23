using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Auth;

/// /internal/* chỉ Gateway gọi. Gateway không có route ra ngoài cho /internal, cộng thêm khoá chung này.
public sealed class InternalApiKeyFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Key";
    private readonly byte[] _expected;

    public InternalApiKeyFilter(IOptions<AuthOptions> options)
        => _expected = Encoding.UTF8.GetBytes(options.Value.InternalApiKey);

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString());
        if (_expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(provided, _expected))
            context.Result = ProblemResponseWriter.ToResult(context.HttpContext, StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthenticated, "Thiếu hoặc sai khoá nội bộ.");
        return Task.CompletedTask;
    }
}
