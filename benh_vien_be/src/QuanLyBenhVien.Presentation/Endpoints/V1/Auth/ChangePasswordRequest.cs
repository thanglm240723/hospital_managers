namespace QuanLyBenhVien.Presentation.Endpoints.V1.Auth;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
