using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Presentation.Http;

public static class ResultExtensions
{
    public static IResult ToProblem(this Error error, HttpContext http)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Precondition => StatusCodes.Status412PreconditionFailed,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        };

        if (error.RetryAfter is { } retryAfter)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            http.Response.Headers.RetryAfter = seconds.ToString();
        }

        return ProblemResponses.Create(http, status, error.Code, error.Message, error.FieldErrors);
    }
}
