namespace CleanArchCqrs.Application.Common.Exceptions;

public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PasswordChangeRequired = "password_change_required";
    public const string CsrfFailed = "csrf_failed";
    public const string NotFound = "not_found";
    public const string EmailTaken = "email_taken";
    public const string LastAdmin = "last_admin";
    public const string SelfActionForbidden = "self_action_forbidden";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string DependencyUnavailable = "dependency_unavailable";
    public const string InternalError = "internal_error";
}
