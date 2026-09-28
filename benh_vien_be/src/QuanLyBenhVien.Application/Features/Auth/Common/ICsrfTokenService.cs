namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface ICsrfTokenService
{
    string Create(Guid sessionFamilyId);

    bool IsValid(Guid sessionFamilyId, string? token);
}
