namespace CleanArchCqrs.Domain.Common;

public interface IDomainEvent
{
	DateTimeOffset OccurredOn { get; }
}

public abstract class DomainEvent : IDomainEvent
{
	public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}