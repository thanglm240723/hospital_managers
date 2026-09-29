namespace QuanLyBenhVien.Application.Common.Results;

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public TimeSpan? RetryAfter { get; init; }

    /// Lỗi theo từng trường form (vd. "currentPassword") để Presentation trả `errors` trong Problem Details.
    public IDictionary<string, string[]>? FieldErrors { get; init; }
}
