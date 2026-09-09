using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserDeactivatedDomainEvent(Guid UserId) : DomainEvent;
