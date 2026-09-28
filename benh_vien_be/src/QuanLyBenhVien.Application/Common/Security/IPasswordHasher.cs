namespace QuanLyBenhVien.Application.Common.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);

    /// Chạy chi phí băm tương đương khi email không tồn tại, để tránh lộ thời gian phản hồi khác biệt.
    void SimulateVerify(string password);
}
