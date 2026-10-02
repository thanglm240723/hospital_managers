using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.CreateUser;

/// InitialPassword là plaintext — chỉ trả đúng một lần trong response tạo (Cache-Control: no-store), không lưu/log/audit.
public sealed record CreateUserResultDto(UserDetailDto User, string InitialPassword);
