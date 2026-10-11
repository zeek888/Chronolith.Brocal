using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>Quota checks before creating users, units, employees, tickets, files or API keys.</summary>
public interface IQuotaGuard
{
    Task<UsageDto> UsageAsync(CancellationToken cancellationToken);

    Task<Result> EnsureCanAddUserAsync(bool portalUser, CancellationToken cancellationToken);

    Task<Result> EnsureCanAddUnitAsync(CancellationToken cancellationToken);

    Task<Result> EnsureCanAddEmployeeAsync(CancellationToken cancellationToken);

    Task<Result> EnsureCanAddTicketAsync(CancellationToken cancellationToken);

    Task<Result> EnsureCanAddApiKeyAsync(CancellationToken cancellationToken);

    Task<Result> EnsureStorageAsync(long additionalBytes, CancellationToken cancellationToken);
}

internal sealed class QuotaGuard(IApplicationDbContext db, ITenantContext tenantContext, ITenantClock clock) : IQuotaGuard
{
    private TenantQuotas Quotas => tenantContext.Tenant.Quotas;

    public async Task<UsageDto> UsageAsync(CancellationToken cancellationToken)
    {
        var monthStart = clock.StartOfDay(new DateOnly(clock.Today.Year, clock.Today.Month, 1));
        var storage = await db.Files.SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;

        return new UsageDto(
            await CountUsersAsync(portal: false, cancellationToken),
            await CountUsersAsync(portal: true, cancellationToken),
            await db.Units.CountAsync(u => u.IsActive, cancellationToken),
            await db.Employees.CountAsync(e => e.IsActive, cancellationToken),
            Math.Round(storage / 1024m / 1024m, 1),
            await db.Tickets.CountAsync(t => t.CreatedAt >= monthStart, cancellationToken),
            await db.ApiKeys.CountAsync(k => !k.IsRevoked, cancellationToken));
    }

    public async Task<Result> EnsureCanAddUserAsync(bool portalUser, CancellationToken cancellationToken)
    {
        var limit = portalUser ? Quotas.MaxPortalUsers : Quotas.MaxUsers;
        if (limit is null)
        {
            return Result.Success();
        }

        return await CountUsersAsync(portalUser, cancellationToken) < limit
            ? Result.Success()
            : TenantErrors.QuotaExceeded(portalUser ? "usuarios del portal" : "usuarios", limit.Value);
    }

    public async Task<Result> EnsureCanAddUnitAsync(CancellationToken cancellationToken) =>
        Quotas.MaxUnits is { } limit && await db.Units.CountAsync(u => u.IsActive, cancellationToken) >= limit
            ? TenantErrors.QuotaExceeded("unidades", limit)
            : Result.Success();

    public async Task<Result> EnsureCanAddEmployeeAsync(CancellationToken cancellationToken) =>
        Quotas.MaxEmployees is { } limit && await db.Employees.CountAsync(e => e.IsActive, cancellationToken) >= limit
            ? TenantErrors.QuotaExceeded("empleados", limit)
            : Result.Success();

    public async Task<Result> EnsureCanAddTicketAsync(CancellationToken cancellationToken)
    {
        if (Quotas.MaxTicketsPerMonth is not { } limit)
        {
            return Result.Success();
        }

        var monthStart = clock.StartOfDay(new DateOnly(clock.Today.Year, clock.Today.Month, 1));
        return await db.Tickets.CountAsync(t => t.CreatedAt >= monthStart, cancellationToken) >= limit
            ? TenantErrors.QuotaExceeded("boletas del mes", limit)
            : Result.Success();
    }

    public async Task<Result> EnsureCanAddApiKeyAsync(CancellationToken cancellationToken) =>
        Quotas.MaxApiKeys is { } limit && await db.ApiKeys.CountAsync(k => !k.IsRevoked, cancellationToken) >= limit
            ? TenantErrors.QuotaExceeded("llaves de API", limit)
            : Result.Success();

    public async Task<Result> EnsureStorageAsync(long additionalBytes, CancellationToken cancellationToken)
    {
        if (Quotas.MaxStorageMb is not { } limitMb)
        {
            return Result.Success();
        }

        var used = await db.Files.SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;
        return used + additionalBytes > limitMb * 1024L * 1024L
            ? TenantErrors.QuotaExceeded("almacenamiento (MB)", limitMb)
            : Result.Success();
    }

    private Task<int> CountUsersAsync(bool portal, CancellationToken cancellationToken) =>
        db.Memberships.CountAsync(m => m.Status != MembershipStatus.Disabled && (portal ? m.CustomerId != null : m.CustomerId == null), cancellationToken);
}
