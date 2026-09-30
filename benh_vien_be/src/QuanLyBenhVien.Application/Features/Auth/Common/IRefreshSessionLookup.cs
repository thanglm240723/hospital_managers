namespace QuanLyBenhVien.Application.Features.Auth.Common;

/// Tra family theo hash refresh token — không khoá, không tracking, nhận diện cả token đã consumed/revoked
/// (đủ để biết cookie thuộc family nào cho quyết định CSRF/logout; không dùng để đọc trạng thái phiên còn hiệu lực).
public interface IRefreshSessionLookup
{
    Task<RefreshSessionRef?> FindAsync(string tokenHash, CancellationToken ct = default);
}
