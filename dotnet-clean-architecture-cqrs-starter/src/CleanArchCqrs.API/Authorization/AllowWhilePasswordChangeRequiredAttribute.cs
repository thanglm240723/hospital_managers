namespace CleanArchCqrs.API.Authorization;

/// Endpoint vẫn dùng được khi tài khoản đang bị bắt đổi mật khẩu (Spec §3.9).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute
{
}
