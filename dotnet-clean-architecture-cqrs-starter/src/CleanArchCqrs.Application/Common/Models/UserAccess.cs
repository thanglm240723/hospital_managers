namespace CleanArchCqrs.Application.Common.Models;

/// Quyền hành động hiệu lực của một user (∪ quyền các role ∪ quyền cấp thêm; rỗng nếu tài khoản bị khoá).
public sealed record UserAccess(IReadOnlySet<string> Permissions, bool MustChangePassword)
{
    public static UserAccess None { get; } = new(new HashSet<string>(), false);
}
