using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    StatusChanged = 3,
    Signed = 4,
    Cancelled = 5,
    Reopened = 6,
    Stamped = 7,
    Exported = 8,
    Accessed = 9
}

/// <summary>
/// Immutable record of who captured, edited or cancelled what (and when). The persistence layer writes one per
/// changed aggregate with the changed fields; business actions (sign, cancel, stamp) add their own with a reason.
/// </summary>
public sealed class AuditEntry : Entity
{
    private AuditEntry()
    {
    }

    public AuditEntry(
        Guid? tenantId,
        DateTimeOffset occurredAt,
        string? userId,
        string userName,
        AuditAction action,
        string entityType,
        Guid? entityId,
        string? summary,
        string? changes,
        string? reason,
        string? correlationId,
        string? ipAddress)
    {
        TenantId = tenantId;
        OccurredAt = occurredAt;
        UserId = userId;
        UserName = userName;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Summary = summary is { Length: > 300 } ? summary[..300] : summary;
        Changes = changes;
        Reason = reason is { Length: > 1000 } ? reason[..1000] : reason;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
    }

    /// <summary>Company of the record (null for platform operations such as creating a company).</summary>
    public Guid? TenantId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string? UserId { get; private set; }

    public string UserName { get; private set; } = null!;

    public AuditAction Action { get; private set; }

    public string EntityType { get; private set; } = null!;

    public Guid? EntityId { get; private set; }

    /// <summary>Human readable reference (folio, code, name).</summary>
    public string? Summary { get; private set; }

    /// <summary>JSON with the changed properties: <c>{ "Status": ["Draft", "Signed"] }</c>.</summary>
    public string? Changes { get; private set; }

    public string? Reason { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }
}

/// <summary>Implemented by aggregates that expose a human readable reference for the audit log.</summary>
public interface IAuditReference
{
    string AuditReference { get; }
}
