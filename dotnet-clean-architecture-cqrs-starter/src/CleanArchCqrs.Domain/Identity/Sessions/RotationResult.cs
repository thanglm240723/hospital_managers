namespace CleanArchCqrs.Domain.Identity.Sessions;

public enum RotationResult
{
    Rotated,
    ReuseDetected,
    Expired,
    NotActive
}
