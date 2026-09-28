namespace QuanLyBenhVien.Gateway.Auth;

public interface ISessionValidator
{
    Task<SessionCheck> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default);
}
