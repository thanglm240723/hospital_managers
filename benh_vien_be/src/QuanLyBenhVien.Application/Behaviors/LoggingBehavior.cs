using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace QuanLyBenhVien.Application.Behaviors;

internal sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly Action<ILogger, string, Exception?> LogHandling =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1000, "HandlingRequest"),
            "Handling {RequestName}");

    private static readonly Action<ILogger, string, long, Exception?> LogHandled =
        LoggerMessage.Define<string, long>(
            LogLevel.Information,
            new EventId(1001, "HandledRequest"),
            "Handled {RequestName} in {ElapsedMilliseconds} ms");

    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1002, "RequestFailed"),
            "Request {RequestName} failed");

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        LogHandling(logger, requestName, null);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            LogHandled(logger, requestName, stopwatch.ElapsedMilliseconds, null);
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            LogFailed(logger, requestName, exception);
            throw;
        }
    }
}
