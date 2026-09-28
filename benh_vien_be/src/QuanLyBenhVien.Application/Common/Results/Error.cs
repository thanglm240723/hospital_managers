namespace QuanLyBenhVien.Application.Common.Results;

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public TimeSpan? RetryAfter { get; init; }
}
