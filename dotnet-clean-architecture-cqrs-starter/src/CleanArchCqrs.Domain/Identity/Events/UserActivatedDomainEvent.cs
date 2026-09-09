using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserActivatedDomainEvent(Guid UserId) : DomainEvent;
