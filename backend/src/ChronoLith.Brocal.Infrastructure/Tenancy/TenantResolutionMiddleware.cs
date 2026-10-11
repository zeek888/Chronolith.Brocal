using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Brocal.Infrastructure.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ChronoLith.Brocal.Infrastructure.Tenancy;

/// <summary>
/// Resolves the company of each authenticated API request from the <c>X-Tenant</c> header (id or slug) and the
/// user's membership in it, and computes the effective permissions:
/// company administrators and platform administrators get every permission of the contracted modules; the rest,
/// the union of their roles. Users with a single company do not need the header.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Tenant";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, IUserContext user, IPlatformAccessService platformAccess, BrocalDbContext db, IMemoryCache cache)
    {
        if (!user.IsAuthenticated || !context.Request.Path.StartsWithSegments("/api") || tenantContext is not TenantContext state)
        {
            await next(context);
            return;
        }

        PlatformAccess access;
        try
        {
            access = await platformAccess.GetAsync(context.RequestAborted);
        }
        catch (PlatformUnavailableException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable, "Plataforma no disponible", ex.Message, "Platform.Unavailable");
            return;
        }

        if (!access.CanAccess)
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Acceso denegado",
                "Tu usuario no tiene acceso a Brocal. Pide al administrador de ChronoLith el permiso 'brocal.access'.", "Platform.NoAccess");
            return;
        }

        var requested = context.Request.Headers[HeaderName].ToString();
        var resolution = await cache.GetOrCreateAsync($"tenant-resolution:{user.IdentityId}:{requested}:{access.CanManagePlatform}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await ResolveAsync(db, user.IdentityId, requested, access.CanManagePlatform, context.RequestAborted);
        });

        if (resolution is { Error: { } error })
        {
            // Endpoints that do not need a company (session, context, platform) keep working without one.
            if (!string.IsNullOrEmpty(requested))
            {
                await WriteProblemAsync(context, error.Status, error.Title, error.Detail, error.Code);
                return;
            }
        }
        else if (resolution is { Tenant: { } tenant })
        {
            state.Set(tenant, resolution.Membership, resolution.IsPlatformAdmin, resolution.Permissions);
        }

        await next(context);
    }

    private static async Task<Resolution> ResolveAsync(BrocalDbContext db, string identityId, string requested, bool isPlatformAdmin, CancellationToken cancellationToken)
    {
        var memberships = await db.Memberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.IdentityId == identityId)
            .ToListAsync(cancellationToken);

        Tenant? tenant;
        if (string.IsNullOrWhiteSpace(requested))
        {
            var active = memberships.Where(m => m.Status == MembershipStatus.Active).ToList();
            if (active.Count != 1)
            {
                return Resolution.Fail(StatusCodes.Status400BadRequest, "Empresa requerida", MembershipErrors.TenantRequired.Description, MembershipErrors.TenantRequired.Code);
            }

            tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == active[0].TenantId, cancellationToken);
        }
        else
        {
            tenant = Guid.TryParse(requested, out var id)
                ? await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                : await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == requested.Trim().ToLower(), cancellationToken);
        }

        if (tenant is null)
        {
            return Resolution.Fail(StatusCodes.Status404NotFound, "Empresa no encontrada", TenantErrors.NotFound.Description, TenantErrors.NotFound.Code);
        }

        var snapshot = TenantSnapshot.From(tenant);
        var membership = memberships.FirstOrDefault(m => m.TenantId == tenant.Id);

        if (membership is null && !isPlatformAdmin)
        {
            return Resolution.Fail(StatusCodes.Status403Forbidden, "Acceso denegado", MembershipErrors.NotFound.Description, MembershipErrors.NotFound.Code);
        }

        if (membership is { Status: MembershipStatus.Disabled } && !isPlatformAdmin)
        {
            return Resolution.Fail(StatusCodes.Status403Forbidden, "Acceso denegado", MembershipErrors.Disabled.Description, MembershipErrors.Disabled.Code);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!tenant.IsOperational(today) && !isPlatformAdmin)
        {
            return Resolution.Fail(StatusCodes.Status403Forbidden, "Empresa suspendida", TenantErrors.NotOperational.Description, TenantErrors.NotOperational.Code);
        }

        IReadOnlySet<string> permissions;
        if (isPlatformAdmin || membership is { IsTenantAdmin: true })
        {
            permissions = TenantContext.AllPermissionsOf(snapshot);
        }
        else
        {
            var roleIds = membership!.RoleIds.ToList();
            var granted = await db.Roles.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.TenantId == tenant.Id && roleIds.Contains(r.Id))
                .Select(r => r.Permissions)
                .ToListAsync(cancellationToken);

            permissions = granted.SelectMany(p => p)
                .Where(p => snapshot.IsModuleEnabled(BrocalModules.ModuleOf(p)))
                .ToHashSet(StringComparer.Ordinal);
        }

        var membershipSnapshot = membership is null
            ? null
            : new MembershipSnapshot(membership.Id, membership.IdentityId, membership.Email, membership.FullName, membership.Status,
                membership.IsTenantAdmin, membership.EmployeeId, membership.CustomerId, membership.RoleIds);

        return new Resolution(snapshot, membershipSnapshot, isPlatformAdmin && membership is null, permissions, null);
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail, string code)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", context.RequestAborted);
    }

    private sealed record ResolutionError(int Status, string Title, string Detail, string Code);

    private sealed record Resolution(TenantSnapshot? Tenant, MembershipSnapshot? Membership, bool IsPlatformAdmin, IReadOnlySet<string> Permissions, ResolutionError? Error)
    {
        public static Resolution Fail(int status, string title, string detail, string code) =>
            new(null, null, false, new HashSet<string>(), new ResolutionError(status, title, detail, code));
    }
}
