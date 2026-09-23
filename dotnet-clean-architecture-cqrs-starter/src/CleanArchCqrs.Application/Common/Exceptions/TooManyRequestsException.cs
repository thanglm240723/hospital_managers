namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class TooManyRequestsException : Exception
{
    public TimeSpan RetryAfter { get; }

    public TooManyRequestsException(TimeSpan retryAfter, string message) : base(message) => RetryAfter = retryAfter;
}
