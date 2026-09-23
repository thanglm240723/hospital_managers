using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArchCqrs.API.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ValidationException validation:
                await ProblemResponseWriter.WriteAsync(context, 400, ErrorCodes.ValidationFailed,
                    "Dữ liệu không hợp lệ.", validation.Errors, ct);
                break;
            case UnauthorizedException unauthorized:
                await ProblemResponseWriter.WriteAsync(context, 401, ErrorCodes.Unauthenticated, unauthorized.Message, ct: ct);
                break;
            case ForbiddenException forbidden:
                await ProblemResponseWriter.WriteAsync(context, 403, forbidden.Code, forbidden.Message, ct: ct);
                break;
            case NotFoundException:
                await ProblemResponseWriter.WriteAsync(context, 404, ErrorCodes.NotFound, "Không tìm thấy tài nguyên.", ct: ct);
                break;
            case ConflictException conflict:
                await ProblemResponseWriter.WriteAsync(context, 409, conflict.Code, conflict.Message, ct: ct);
                break;
            case BusinessRuleViolationException rule:
                await ProblemResponseWriter.WriteAsync(context, 409, ErrorCodes.Conflict, rule.Message, ct: ct);
                break;
            case TooManyRequestsException tooMany:
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(tooMany.RetryAfter.TotalSeconds)).ToString();
                await ProblemResponseWriter.WriteAsync(context, 429, ErrorCodes.RateLimited, tooMany.Message, ct: ct);
                break;
            default:
                _logger.LogError(exception, "Unhandled exception");
                await ProblemResponseWriter.WriteAsync(context, 500, ErrorCodes.InternalError, "Đã có lỗi xảy ra.", ct: ct);
                break;
        }

        return true;
    }
}
