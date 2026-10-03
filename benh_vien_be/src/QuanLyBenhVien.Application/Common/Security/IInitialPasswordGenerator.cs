namespace QuanLyBenhVien.Application.Common.Security;

/// Sinh mật khẩu ban đầu ngẫu nhiên cho tài khoản do admin tạo. Plaintext chỉ trả về một lần trong response tạo.
public interface IInitialPasswordGenerator
{
    string Generate();
}
