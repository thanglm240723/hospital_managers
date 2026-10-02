namespace QuanLyBenhVien.Domain.Exceptions;

/// Vi phạm unique constraint (PostgreSQL 23505) do Persistence dịch từ DbUpdateException để Application
/// phân biệt đúng constraint thay vì bắt mọi lỗi ghi.
public sealed class UniqueConstraintViolationException(string? constraintName, Exception inner)
    : Exception($"Unique constraint '{constraintName}' violated.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
