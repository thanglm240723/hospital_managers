namespace QuanLyBenhVien.Application.Common.Identity;

public interface IRequestContext
{
    string? CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
