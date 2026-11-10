using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Licensing;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>
/// A limit of the ChronoLith license was reached. The API answers 403 with the problem type
/// <c>LicenseProblemTypes.LimitReached</c> and the extensions <c>key</c> and <c>limit</c>.
/// </summary>
public sealed record LicenseLimitError(string Key, int Limit, string Message)
    : Error("License.LimitReached", Message, ErrorType.Forbidden);

/// <summary>
/// Limits of the ChronoLith license of the installation (standalone mode). The license belongs to the installation,
/// not to a company: the counts span every company and add to the quotas the platform sets per company. A key the
/// license does not carry has no limit. In module mode the Shell validates the license and nothing is registered
/// here, so every check passes.
/// </summary>
public interface ILicenseLimits
{
    /// <summary>Distinct people (e-mail) with an enabled access (invited or active) as staff of any company.</summary>
    public const string Users = "users";

    /// <summary>Distinct people (e-mail) with an enabled access to the customer portal of any company.</summary>
    public const string PortalUsers = "portalusers";

    /// <summary>Companies that are active or in trial (suspended and cancelled ones free their place).</summary>
    public const string Companies = "companies";

    /// <summary>Active units and equipment.</summary>
    public const string Units = "units";

    /// <summary>Active employees (personnel records).</summary>
    public const string Employees = "employees";

    /// <summary>Service tickets created in the current calendar month (UTC).</summary>
    public const string Tickets = "tickets";

    /// <summary>Megabytes of stored files.</summary>
    public const string StorageMb = "storagemb";

    /// <summary>API keys that are not revoked.</summary>
    public const string ApiKeys = "apikeys";

    /// <summary>Every limit Brocal understands, in the order of the "Licencia" page.</summary>
    public static readonly IReadOnlyList<string> All = [Users, PortalUsers, Companies, Units, Employees, Tickets, StorageMb, ApiKeys];

    /// <summary>Before giving access to <paramref name="email"/>: someone who already counts (another company) fits.</summary>
    Task<Result> EnsureCanAddUserAsync(string email, bool portalUser, CancellationToken cancellationToken);

    /// <summary>Before creating a company or reactivating a suspended or cancelled one.</summary>
    Task<Result> EnsureCanAddCompanyAsync(CancellationToken cancellationToken);

    /// <summary>Before adding one more of <see cref="Units"/>, <see cref="Employees"/>, <see cref="Tickets"/> or <see cref="ApiKeys"/>.</summary>
    Task<Result> EnsureCanAddAsync(string key, CancellationToken cancellationToken);

    /// <summary>Before storing <paramref name="additionalBytes"/> more.</summary>
    Task<Result> EnsureStorageAsync(long additionalBytes, CancellationToken cancellationToken);

    /// <summary>Current usage of every limit (for the "Licencia" page).</summary>
    Task<IReadOnlyDictionary<string, int>> UsageAsync(CancellationToken cancellationToken);
}

/// <param name="license">Null in module mode (the licensing services are registered only in standalone mode).</param>
internal sealed class LicenseLimits(IApplicationDbContext db, TimeProvider timeProvider, ILicenseAccessor? license = null) : ILicenseLimits
{
    private const long Megabyte = 1024L * 1024L;

    /// <summary>
    /// Active license; null in module mode and before it is loaded (the database is migrated and the demo company
    /// seeded first), when nothing is limited.
    /// </summary>
    private LicenseSnapshot? Current
    {
        get
        {
            if (license is null)
            {
                return null;
            }

            try
            {
                return license.Current;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public async Task<Result> EnsureCanAddUserAsync(string email, bool portalUser, CancellationToken cancellationToken)
    {
        var key = portalUser ? ILicenseLimits.PortalUsers : ILicenseLimits.Users;
        if (Current?.Limit(key) is not { } limit)
        {
            return Result.Success();
        }

        var normalized = TenantMembership.NormalizeEmail(email) ?? email.Trim().ToLowerInvariant();
        var counted = await People(portalUser).ToListAsync(cancellationToken);
        return counted.Contains(normalized) || counted.Count < limit
            ? Result.Success()
            : LimitReached(key, limit);
    }

    public async Task<Result> EnsureCanAddCompanyAsync(CancellationToken cancellationToken) =>
        await EnsureCanAddAsync(ILicenseLimits.Companies, cancellationToken);

    public async Task<Result> EnsureCanAddAsync(string key, CancellationToken cancellationToken)
    {
        if (Current?.Limit(key) is not { } limit)
        {
            return Result.Success();
        }

        return await CountAsync(key, cancellationToken) < limit
            ? Result.Success()
            : LimitReached(key, limit);
    }

    public async Task<Result> EnsureStorageAsync(long additionalBytes, CancellationToken cancellationToken)
    {
        if (Current?.Limit(ILicenseLimits.StorageMb) is not { } limitMb)
        {
            return Result.Success();
        }

        return await StoredBytesAsync(cancellationToken) + additionalBytes > limitMb * Megabyte
            ? LimitReached(ILicenseLimits.StorageMb, limitMb)
            : Result.Success();
    }

    public async Task<IReadOnlyDictionary<string, int>> UsageAsync(CancellationToken cancellationToken)
    {
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in ILicenseLimits.All)
        {
            usage[key] = await CountAsync(key, cancellationToken);
        }

        return usage;
    }

    private async Task<int> CountAsync(string key, CancellationToken cancellationToken) => key switch
    {
        ILicenseLimits.Users => await People(portal: false).CountAsync(cancellationToken),
        ILicenseLimits.PortalUsers => await People(portal: true).CountAsync(cancellationToken),
        ILicenseLimits.Companies => await db.Tenants.AsNoTracking().CountAsync(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial, cancellationToken),
        ILicenseLimits.Units => await db.Units.IgnoreQueryFilters().CountAsync(u => u.IsActive, cancellationToken),
        ILicenseLimits.Employees => await db.Employees.IgnoreQueryFilters().CountAsync(e => e.IsActive, cancellationToken),
        ILicenseLimits.Tickets => await TicketsThisMonthAsync(cancellationToken),
        ILicenseLimits.ApiKeys => await db.ApiKeys.IgnoreQueryFilters().CountAsync(k => !k.IsRevoked, cancellationToken),
        ILicenseLimits.StorageMb => (int)Math.Ceiling(await StoredBytesAsync(cancellationToken) / (double)Megabyte),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "LÃ­mite de licencia desconocido.")
    };

    private Task<int> TicketsThisMonthAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return db.Tickets.IgnoreQueryFilters().CountAsync(t => t.CreatedAt >= monthStart, cancellationToken);
    }

    // Every company (no tenant filter): the license is shared by the whole installation.
    private IQueryable<string> People(bool portal) =>
        db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Status != MembershipStatus.Disabled && (portal ? m.CustomerId != null : m.CustomerId == null))
            .Select(m => m.Email)
            .Distinct();

    private async Task<long> StoredBytesAsync(CancellationToken cancellationToken) =>
        await db.Files.IgnoreQueryFilters().SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;

    private static LicenseLimitError LimitReached(string key, int limit) => new(key, limit, key switch
    {
        ILicenseLimits.Users => $"La licencia permite {limit} usuarios activos.",
        ILicenseLimits.PortalUsers => $"La licencia permite {limit} usuarios del portal del cliente.",
        ILicenseLimits.Companies => $"La licencia permite {limit} empresas activas.",
        ILicenseLimits.Units => $"La licencia permite {limit} unidades activas en la instalaciÃ³n.",
        ILicenseLimits.Employees => $"La licencia permite {limit} empleados activos en la instalaciÃ³n.",
        ILicenseLimits.Tickets => $"La licencia permite {limit} boletas por mes en la instalaciÃ³n.",
        ILicenseLimits.StorageMb => $"La licencia permite {limit} MB de archivos en la instalaciÃ³n.",
        ILicenseLimits.ApiKeys => $"La licencia permite {limit} llaves de API activas.",
        _ => $"Se alcanzÃ³ el lÃ­mite '{key}' de la licencia ({limit})."
    });
}
