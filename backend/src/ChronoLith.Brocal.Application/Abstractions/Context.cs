using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Abstractions;

/// <summary>Signed-in user (Keycloak token).</summary>
public interface IUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>Keycloak subject (<c>sub</c>).</summary>
    string IdentityId { get; }

    string Username { get; }

    string? Email { get; }

    /// <summary>Keycloak <c>email_verified</c>: invitations bind only to verified addresses.</summary>
    bool EmailVerified { get; }

    string? FullName { get; }

    /// <summary>Name for audit columns and documents.</summary>
    string DisplayName { get; }

    bool IsInRole(string role);

    string? IpAddress { get; }

    string? CorrelationId { get; }
}

/// <summary>What the user can do at platform level (ChronoLith Shell permissions or realm roles when standalone).</summary>
public sealed record PlatformAccess(bool CanAccess, bool CanManagePlatform)
{
    public static PlatformAccess None { get; } = new(false, false);
}

public interface IPlatformAccessService
{
    Task<PlatformAccess> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Immutable view of the current company (loaded once per request and cached).</summary>
public sealed record TenantSnapshot(
    Guid Id,
    string Slug,
    string Name,
    string? LegalName,
    string? TaxId,
    TenantStatus Status,
    string Plan,
    DateOnly? TrialEndsOn,
    TenantSettings Settings,
    TenantBranding Branding,
    TenantQuotas Quotas,
    TenantFiscalProfile Fiscal,
    IReadOnlyList<string> EnabledModules)
{
    public static TenantSnapshot From(Tenant tenant) => new(
        tenant.Id,
        tenant.Slug,
        tenant.Name,
        tenant.LegalName,
        tenant.TaxId,
        tenant.Status,
        tenant.Plan,
        tenant.TrialEndsOn,
        tenant.Settings,
        tenant.Branding,
        tenant.Quotas,
        tenant.Fiscal,
        tenant.EnabledModules.ToList());

    public bool IsModuleEnabled(string module) => BrocalModules.CoreModules.Contains(module) || EnabledModules.Contains(module);
}

/// <summary>Membership of the current user in the current company.</summary>
public sealed record MembershipSnapshot(
    Guid Id,
    string? IdentityId,
    string Email,
    string FullName,
    MembershipStatus Status,
    bool IsTenantAdmin,
    Guid? EmployeeId,
    Guid? CustomerId,
    IReadOnlyList<Guid> RoleIds);

/// <summary>
/// The company the request works on (header <c>X-Tenant</c>) plus the effective permissions of the user in it.
/// The persistence layer filters every tenant-owned query with <see cref="CurrentTenantId"/>.
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <summary>Current company id or <see cref="Guid.Empty"/> (which matches no row).</summary>
    Guid CurrentTenantId { get; }

    /// <summary>Current company id; throws when the request has no company.</summary>
    Guid TenantId { get; }

    TenantSnapshot Tenant { get; }

    /// <summary>Null for platform administrators without membership and for background jobs.</summary>
    MembershipSnapshot? Membership { get; }

    /// <summary>Platform administrator acting on the company (every permission).</summary>
    bool IsPlatformAdmin { get; }

    IReadOnlySet<string> Permissions { get; }

    bool Has(string permission);

    bool HasAny(params string[] permissions);

    bool IsModuleEnabled(string module);

    /// <summary>Acts as the system inside a company (background jobs, seeding, platform operations).</summary>
    IDisposable BeginScope(TenantSnapshot tenant);
}

/// <summary>"Today" and "now" in the time zone of the current company.</summary>
public interface ITenantClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }

    DateTimeOffset LocalNow { get; }

    TimeZoneInfo TimeZone { get; }

    /// <summary>First instant (UTC) of a local date.</summary>
    DateTimeOffset StartOfDay(DateOnly date);
}

/// <summary>Encrypts secrets at rest (PAC credentials, SMTP passwords, WhatsApp tokens).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string? Unprotect(string? protectedText);
}

/// <summary>Writes explicit business actions to the audit log (automatic changes are captured by the persistence layer).</summary>
public interface IAuditLog
{
    void Record(AuditAction action, string entityType, Guid? entityId, string? summary, string? reason = null, object? details = null);
}
