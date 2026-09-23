using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

/// Tài khoản MustChangePassword chỉ được gọi endpoint có [AllowWhilePasswordChangeRequired].
public sealed class PasswordChangeRequirement : IAuthorizationRequirement
{
}
