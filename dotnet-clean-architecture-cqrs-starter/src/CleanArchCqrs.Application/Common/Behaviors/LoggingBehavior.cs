using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CleanArchCqrs.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            var timeout = stopwatch.ElapsedMilliseconds;
            if (timeout > 500)
            {
                _logger.LogWarning(
                    "Long Running Request: {RequestName} ({ElapsedMilliseconds} ms)",
                    requestName,
                    timeout);
            }
            else
            {
                _logger.LogInformation(
                    "Request: {RequestName} ({ElapsedMilliseconds} ms)",
                    requestName,
                    timeout);

            }


        }
    }
}
