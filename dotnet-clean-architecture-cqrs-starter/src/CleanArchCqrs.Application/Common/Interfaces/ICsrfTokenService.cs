namespace CleanArchCqrs.Application.Common.Interfaces;

/// CSRF token có chữ ký gắn với phiên (SessionFamily) — Đặc tả kỹ thuật §4.1.
public interface ICsrfTokenService
{
    string Create(Guid sessionFamilyId);
    bool IsValid(Guid sessionFamilyId, string? token);
}
