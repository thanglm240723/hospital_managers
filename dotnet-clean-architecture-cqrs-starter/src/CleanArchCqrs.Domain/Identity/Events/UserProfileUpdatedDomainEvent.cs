using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserProfileUpdatedDomainEvent(Guid UserId) : DomainEvent;
