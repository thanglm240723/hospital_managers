namespace QuanLyBenhVien.Application.Common.Identity;

/// Đọc quyền hiệu lực trực tiếp từ DB (nguồn sự thật). Trả null nếu user không tồn tại.
public interface IUserAccessReadService
{
    Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct);
}
