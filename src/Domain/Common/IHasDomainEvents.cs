namespace Domain.Common;

/// <summary>
/// Non-generic view of an aggregate's pending domain events, so that the unit of work can collect them
/// from every aggregate whatever its id type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Returns the pending events and clears them.</summary>
    IReadOnlyCollection<IDomainEvent> PopDomainEvents();
}
