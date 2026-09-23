namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class ForbiddenException : Exception
{
    public string Code { get; }

    public ForbiddenException(string code, string message) : base(message) => Code = code;
}
