using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.SharedCatalogs;

// ---- Lookups for the forms (any member of a company) ------------------------------------------------------

/// <summary>Elements of a shared catalog of the Shell, from the replica, in the shape of every other lookup.</summary>
public sealed record GetSharedCatalogLookupQuery(string Catalog, bool ActiveOnly, Guid? ParentId) : IQuery<IReadOnlyList<LookupDto>>;

internal sealed class GetSharedCatalogLookupQueryHandler(ISharedCatalogs catalogs) : IQueryHandler<GetSharedCatalogLookupQuery, IReadOnlyList<LookupDto>>
{
    public async Task<Result<IReadOnlyList<LookupDto>>> Handle(GetSharedCatalogLookupQuery query, CancellationToken cancellationToken)
    {
        if (!SharedCatalogNames.IsValid(query.Catalog))
        {
            return SharedCatalogErrors.UnknownCatalog;
        }

        var items = await catalogs.LookupAsync(query.Catalog, query.ActiveOnly, query.ParentId, cancellationToken);
        return items.Select(i => new LookupDto(i.Id, i.Code, i.Name, i.IsActive, i.ParentId)).ToList();
    }
}

/// <summary>Whether the forms should offer the ChronoLith catalogs (connected to a Shell with a service account).</summary>
public sealed record SharedCatalogAvailabilityDto(bool Available);

public sealed record GetSharedCatalogAvailabilityQuery : IQuery<SharedCatalogAvailabilityDto>;

internal sealed class GetSharedCatalogAvailabilityQueryHandler(ISharedCatalogs catalogs) : IQueryHandler<GetSharedCatalogAvailabilityQuery, SharedCatalogAvailabilityDto>
{
    public Task<Result<SharedCatalogAvailabilityDto>> Handle(GetSharedCatalogAvailabilityQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(new SharedCatalogAvailabilityDto(catalogs.IsAvailable)));
}

// ---- Publish a local record in the master (catalogs.manage) --------------------------------------------

public sealed record CatalogPushResultDto(CatalogPushOutcome Outcome, Guid? ShellId, string? Detail)
{
    public static CatalogPushResultDto From(CatalogPushResult result) => new(result.Outcome, result.ShellId, result.Detail);
}

/// <summary>Creates the site in the master catalog of ChronoLith (or links it when its code already exists there) and pushes its shared fields.</summary>
public sealed record PublishSiteToShellCommand(Guid Id) : ICommand<CatalogPushResultDto>;

internal sealed class PublishSiteToShellCommandHandler(IApplicationDbContext db, MasterCatalogSync sync) : ICommandHandler<PublishSiteToShellCommand, CatalogPushResultDto>
{
    public async Task<Result<CatalogPushResultDto>> Handle(PublishSiteToShellCommand command, CancellationToken cancellationToken)
    {
        if (!sync.Enabled)
        {
            return SharedCatalogErrors.NotAvailable;
        }

        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (site is null)
        {
            return Domain.Common.CatalogErrors.NotFound(Domain.Commercial.SiteErrors.Catalog);
        }

        var result = await sync.PushSiteAsync(site, allowCreate: true, cancellationToken);
        return result.IsFailure ? result.Error : CatalogPushResultDto.From(result.Value);
    }
}

public sealed record PublishCustomerToShellCommand(Guid Id) : ICommand<CatalogPushResultDto>;

internal sealed class PublishCustomerToShellCommandHandler(IApplicationDbContext db, MasterCatalogSync sync) : ICommandHandler<PublishCustomerToShellCommand, CatalogPushResultDto>
{
    public async Task<Result<CatalogPushResultDto>> Handle(PublishCustomerToShellCommand command, CancellationToken cancellationToken)
    {
        if (!sync.Enabled)
        {
            return SharedCatalogErrors.NotAvailable;
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
        if (customer is null)
        {
            return Domain.Common.CatalogErrors.NotFound(Domain.Commercial.CustomerErrors.Catalog);
        }

        var result = await sync.PushCustomerAsync(customer, allowCreate: true, cancellationToken);
        return result.IsFailure ? result.Error : CatalogPushResultDto.From(result.Value);
    }
}

// ---- Administration of the replica (platform administrators) -------------------------------------------

public sealed record SharedCatalogCountDto(string Catalog, int Total, int Active);

public sealed record CatalogPushDto(
    Guid Id,
    string TenantSlug,
    string TenantName,
    string EntityType,
    Guid EntityId,
    string Catalog,
    string Method,
    Guid? ShellId,
    string Summary,
    CatalogPushStatus Status,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? SentAt,
    string? LastError);

public sealed record SharedCatalogStatusDto(
    bool Available,
    bool BackgroundEnabled,
    int IntervalMinutes,
    long Cursor,
    long Latest,
    DateTimeOffset? LastPolledAt,
    DateTimeOffset? LastReportedAt,
    DateTimeOffset? LastFullSyncAt,
    int LastRunApplied,
    int LastRunErrors,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    IReadOnlyList<SharedCatalogCountDto> Counts,
    int PendingPushes,
    int RejectedPushes,
    int FailedPushes,
    IReadOnlyList<CatalogPushDto> Pushes);

public sealed record GetSharedCatalogStatusQuery : IQuery<SharedCatalogStatusDto>;

internal sealed class GetSharedCatalogStatusQueryHandler(IApplicationDbContext db, ISharedCatalogs catalogs, ICatalogSyncRunner runner) : IQueryHandler<GetSharedCatalogStatusQuery, SharedCatalogStatusDto>
{
    public async Task<Result<SharedCatalogStatusDto>> Handle(GetSharedCatalogStatusQuery query, CancellationToken cancellationToken)
    {
        var state = await db.CatalogFeedStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == CatalogFeedState.SingletonId, cancellationToken);
        var counts = await db.SharedCatalogItems.AsNoTracking()
            .GroupBy(i => i.Catalog)
            .Select(g => new SharedCatalogCountDto(g.Key, g.Count(), g.Count(i => i.IsActive)))
            .ToListAsync(cancellationToken);
        var ordered = SharedCatalogNames.All.Select(c => counts.FirstOrDefault(x => x.Catalog == c) ?? new SharedCatalogCountDto(c, 0, 0)).ToList();

        var pushes = await db.CatalogPushMessages.AsNoTracking()
            .Where(m => m.Status != CatalogPushStatus.Sent && m.Status != CatalogPushStatus.Discarded)
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var tenantIds = pushes.Select(p => p.TenantId).Distinct().ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => new { t.Slug, t.Name }, cancellationToken);

        return new SharedCatalogStatusDto(
            catalogs.IsAvailable,
            runner.BackgroundEnabled,
            runner.IntervalMinutes,
            state?.Cursor ?? 0,
            state?.Latest ?? 0,
            state?.LastPolledAt,
            state?.LastReportedAt,
            state?.LastFullSyncAt,
            state?.LastRunApplied ?? 0,
            state?.LastRunErrors ?? 0,
            state?.LastError,
            state?.LastErrorAt,
            ordered,
            pushes.Count(p => p.Status == CatalogPushStatus.Pending),
            pushes.Count(p => p.Status == CatalogPushStatus.Rejected),
            pushes.Count(p => p.Status == CatalogPushStatus.Failed),
            pushes.Select(p => new CatalogPushDto(
                p.Id,
                tenants.TryGetValue(p.TenantId, out var tenant) ? tenant.Slug : string.Empty,
                tenant?.Name ?? string.Empty,
                p.EntityType,
                p.EntityId,
                p.Catalog,
                p.Method,
                p.ShellId,
                p.Summary,
                p.Status,
                p.Attempts,
                p.CreatedAt,
                p.NextAttemptAt,
                p.SentAt,
                p.LastError)).ToList());
    }
}

/// <summary>"Consultar ahora" / "Recargar completo" of the platform administrator.</summary>
public sealed record RunCatalogSyncCommand(bool FullReload) : ICommand<CatalogSyncRunSummary>;

internal sealed class RunCatalogSyncCommandHandler(ICatalogSyncRunner runner, ISharedCatalogs catalogs) : ICommandHandler<RunCatalogSyncCommand, CatalogSyncRunSummary>
{
    public async Task<Result<CatalogSyncRunSummary>> Handle(RunCatalogSyncCommand command, CancellationToken cancellationToken)
    {
        if (!catalogs.IsAvailable)
        {
            return SharedCatalogErrors.NotAvailable;
        }

        return await runner.RunAsync(command.FullReload, cancellationToken) is { } summary ? summary : SharedCatalogErrors.SyncRunning;
    }
}

public sealed record RetryCatalogPushCommand(Guid Id) : ICommand;

internal sealed class RetryCatalogPushCommandValidator : AbstractValidator<RetryCatalogPushCommand>
{
    public RetryCatalogPushCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class RetryCatalogPushCommandHandler(IApplicationDbContext db, TimeProvider clock) : ICommandHandler<RetryCatalogPushCommand>
{
    public async Task<Result> Handle(RetryCatalogPushCommand command, CancellationToken cancellationToken)
    {
        var push = await db.CatalogPushMessages.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (push is null)
        {
            return SharedCatalogErrors.PushNotFound;
        }

        push.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DiscardCatalogPushCommand(Guid Id) : ICommand;

internal sealed class DiscardCatalogPushCommandHandler(IApplicationDbContext db, IUserContext user, TimeProvider clock) : ICommandHandler<DiscardCatalogPushCommand>
{
    public async Task<Result> Handle(DiscardCatalogPushCommand command, CancellationToken cancellationToken)
    {
        var push = await db.CatalogPushMessages.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (push is null)
        {
            return SharedCatalogErrors.PushNotFound;
        }

        push.Discard(clock.GetUtcNow(), $"descartado por {user.DisplayName}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
