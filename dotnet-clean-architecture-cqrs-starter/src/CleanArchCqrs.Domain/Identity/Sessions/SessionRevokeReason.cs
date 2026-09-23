namespace CleanArchCqrs.Domain.Identity.Sessions;

public enum SessionRevokeReason
{
    Logout,
    LogoutAll,
    Reuse,
    UserRevoked,
    PasswordChanged,
    AccountDeactivated
}
