namespace ChronoLith.Brocal.Domain.Abstractions;

public abstract class Entity
{
    protected Entity() => Id = Guid.CreateVersion7();

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; private init; }
}

/// <summary>Marker for something meaningful that happened inside an aggregate.</summary>
public interface IDomainEvent;

/// <summary>Audit columns populated by the persistence layer, never by business code.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    string? CreatedBy { get; }

    DateTimeOffset? UpdatedAt { get; }

    string? UpdatedBy { get; }
}

/// <summary>
/// Row that belongs to one company (tenant). The persistence layer stamps <see cref="TenantId"/> on insert,
/// filters every query by the current tenant and rejects writes that cross tenants.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>
/// Consistency boundary. Only aggregate roots are loaded/saved through the persistence layer;
/// they collect domain events that are dispatched after a successful commit.
/// </summary>
public abstract class AggregateRoot : Entity, IAuditable
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL <c>xmin</c>).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>Aggregate root owned by a tenant (every business record of Brocal).</summary>
public abstract class TenantAggregateRoot : AggregateRoot, ITenantOwned
{
    protected TenantAggregateRoot()
    {
    }

    protected TenantAggregateRoot(Guid id)
        : base(id)
    {
    }

    public Guid TenantId { get; private set; }
}

/// <summary>Child entity of a tenant aggregate; it carries the tenant too so direct queries stay isolated.</summary>
public abstract class TenantEntity : Entity, ITenantOwned
{
    protected TenantEntity()
    {
    }

    protected TenantEntity(Guid id)
        : base(id)
    {
    }

    public Guid TenantId { get; private set; }
}
