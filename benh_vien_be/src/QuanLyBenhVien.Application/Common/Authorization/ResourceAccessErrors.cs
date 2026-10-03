using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Common.Authorization;

public static class ResourceAccessErrors
{
    public static readonly Error NotFound = new("resource_not_found", "Không tìm thấy hoặc bạn không có quyền truy cập.", ErrorType.NotFound);
}
