namespace QuanLyBenhVien.Application.Features.Auth.Common;

/// Kiểm tra header Origin của request đổi trạng thái phiên thuộc allowlist cấu hình (spec V2 §5.3).
public interface IRequestOriginPolicy
{
    bool IsAllowed(string? origin);
}
