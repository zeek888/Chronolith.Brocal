using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Infrastructure.Tenancy;

/// <summary>
/// Scoped state of the current company. The resolution middleware sets it once per request; background jobs and
/// platform operations push a temporary scope with <see cref="BeginScope"/>.
/// </summary>
internal sealed class TenantContext : ITenantContext
{
    private readonly Stack<State> _scopes = new();
    private State? _state;

    private State? Current => _scopes.Count > 0 ? _scopes.Peek() : _state;

    public bool HasTenant => Current is not null;

    public Guid CurrentTenantId => Current?.Tenant.Id ?? Guid.Empty;

    public Guid TenantId => Current?.Tenant.Id ?? throw new InvalidOperationException("La solicitud no tiene una empresa seleccionada.");

    public TenantSnapshot Tenant => Current?.Tenant ?? throw new InvalidOperationException("La solicitud no tiene una empresa seleccionada.");

    public MembershipSnapshot? Membership => Current?.Membership;

    public bool IsPlatformAdmin => Current?.IsPlatformAdmin ?? false;

    public IReadOnlySet<string> Permissions => Current?.Permissions ?? EmptySet;

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();

    public bool Has(string permission) => Permissions.Contains(permission);

    public bool HasAny(params string[] permissions) => permissions.Any(Has);

    public bool IsModuleEnabled(string module) => Current?.Tenant.IsModuleEnabled(module) ?? false;

    /// <summary>Sets the company of the request (once, by the resolution middleware).</summary>
    public void Set(TenantSnapshot tenant, MembershipSnapshot? membership, bool isPlatformAdmin, IReadOnlySet<string> permissions) =>
        _state = new State(tenant, membership, isPlatformAdmin, permissions);

    public IDisposable BeginScope(TenantSnapshot tenant)
    {
        _scopes.Push(new State(tenant, null, true, AllPermissionsOf(tenant)));
        return new Scope(this);
    }

    /// <summary>Every permission of the modules the company contracted.</summary>
    public static IReadOnlySet<string> AllPermissionsOf(TenantSnapshot tenant) =>
        BrocalModules.All
            .Where(m => tenant.IsModuleEnabled(m.Code))
            .SelectMany(m => m.Permissions)
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);

    private sealed record State(TenantSnapshot Tenant, MembershipSnapshot? Membership, bool IsPlatformAdmin, IReadOnlySet<string> Permissions);

    private sealed class Scope(TenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed && owner._scopes.Count > 0)
            {
                owner._scopes.Pop();
            }

            _disposed = true;
        }
    }
}
