using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId, string Email, string Role) : DomainEvent;
