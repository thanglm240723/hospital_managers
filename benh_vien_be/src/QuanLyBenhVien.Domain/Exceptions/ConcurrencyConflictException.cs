namespace QuanLyBenhVien.Domain.Exceptions;

/// Xung đột concurrency token (xmin) do Persistence dịch từ DbUpdateConcurrencyException
/// để Application trả lỗi 412 thay vì để lọt thành 500.
public sealed class ConcurrencyConflictException(Exception inner)
    : Exception("Dữ liệu đã bị thay đổi bởi giao dịch khác.", inner);
