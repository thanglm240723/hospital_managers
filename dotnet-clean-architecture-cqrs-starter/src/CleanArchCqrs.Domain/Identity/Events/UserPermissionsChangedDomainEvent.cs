using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserPermissionsChangedDomainEvent(Guid UserId) : DomainEvent;
