namespace CleanArchCqrs.Application.Common.Interfaces;

/// Thông tin kỹ thuật của request hiện tại cho audit. Ngoài HTTP (worker, seed) mọi giá trị là null.
public interface IRequestContext
{
    string? CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
