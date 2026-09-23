using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserRolesChangedDomainEvent(Guid UserId) : DomainEvent;
