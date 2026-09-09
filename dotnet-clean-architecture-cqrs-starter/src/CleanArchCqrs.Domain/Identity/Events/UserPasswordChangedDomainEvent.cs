using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity.Events;

public sealed record UserPasswordChangedDomainEvent(Guid UserId) : DomainEvent;
