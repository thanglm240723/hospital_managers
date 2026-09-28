using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.API.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var result = exception switch
        {
            ValidationException validation => ProblemResponses.Create(context, StatusCodes.Status400BadRequest,
                "validation_failed", "Dữ liệu không hợp lệ.", ToErrors(validation)),
            BadHttpRequestException => ProblemResponses.Create(context, StatusCodes.Status400BadRequest,
                "validation_failed", "Dữ liệu không hợp lệ."),
            NotFoundException notFound => ProblemResponses.Create(context, StatusCodes.Status404NotFound,
                "not_found", notFound.Message),
            BusinessRuleViolationException rule => ProblemResponses.Create(context, StatusCodes.Status409Conflict,
                "conflict", rule.Message),
            _ => LogAndFallback(context, exception),
        };

        await result.ExecuteAsync(context);
        return true;
    }

    private IResult LogAndFallback(HttpContext context, Exception exception)
    {
        logger.LogError(exception, "Unhandled exception");
        return ProblemResponses.Create(context, StatusCodes.Status500InternalServerError,
            "internal_error", "Đã có lỗi xảy ra.");
    }

    private static IDictionary<string, string[]> ToErrors(ValidationException validation)
        => validation.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
}
