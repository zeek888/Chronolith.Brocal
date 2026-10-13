using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Automation;

// ---- Catalog ----------------------------------------------------------------------------------------------

public sealed record AutomationTriggerDto(AutomationTrigger Trigger, string Name, string NameEn);

public sealed record AutomationDocumentDto(
    AutomationDocument Document, string Module, bool Enabled, string Name, string NameEn, string Icon, IReadOnlyList<AutomationTrigger> Triggers, WorkflowGate? Workflow,
    IReadOnlyList<FactDefinition> Facts);

public sealed record RoleTemplateDto(string Key, string Name);

/// <summary>What the portal needs to build conditions: documents, moments, data of each document and role templates.</summary>
public sealed record AutomationCatalogDto(
    IReadOnlyList<AutomationDocumentDto> Documents, IReadOnlyList<AutomationTriggerDto> Triggers, IReadOnlyList<RoleTemplateDto> RoleTemplates, bool CanManage, bool CanManageGlobal);

public sealed record GetAutomationCatalogQuery : IQuery<AutomationCatalogDto>;

internal sealed class GetAutomationCatalogQueryHandler(ITenantContext tenant, IPlatformAccessService platform) : IQueryHandler<GetAutomationCatalogQuery, AutomationCatalogDto>
{
    public async Task<Result<AutomationCatalogDto>> Handle(GetAutomationCatalogQuery query, CancellationToken cancellationToken)
    {
        var access = await platform.GetAsync(cancellationToken);
        return new AutomationCatalogDto(
            AutomationCatalog.All.Select(d => new AutomationDocumentDto(d.Document, d.Module, tenant.IsModuleEnabled(d.Module), d.Name, d.NameEn, d.Icon, d.Triggers, d.Workflow, d.Facts)).ToList(),
            AutomationCatalog.TriggerNames.Select(t => new AutomationTriggerDto(t.Key, t.Value.Name, t.Value.NameEn)).ToList(),
            RoleTemplates.All.Where(t => t.Key != "customer").Select(t => new RoleTemplateDto(t.Key, t.Name)).ToList(),
            tenant.Has(Permissions.AutomationManage),
            access.CanManagePlatform);
    }
}

/// <summary>Company definitions are managed with <c>automation.manage</c>; global ones only by the platform administrator.</summary>
internal sealed class AutomationAuthority(ITenantContext tenant, IPlatformAccessService platform, IApplicationDbContext db)
{
    public async Task<bool> CanManageGlobalAsync(CancellationToken cancellationToken) => (await platform.GetAsync(cancellationToken)).CanManagePlatform;

    public bool CanManageCompany => tenant.Has(Permissions.AutomationManage);

    /// <summary>Roles named by a company definition must be roles of the company.</summary>
    public async Task<Result> EnsureRolesAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToList();
        return ids.Count == 0 || await db.Roles.CountAsync(r => ids.Contains(r.Id), cancellationToken) == ids.Count
            ? Result.Success()
            : RoleErrors.NotFound;
    }
}

// ---- Workflows --------------------------------------------------------------------------------------------

public sealed record WorkflowDefinitionDto(
    Guid Id, bool IsGlobal, string Name, string? Description, AutomationDocument Document, bool IsEnabled, bool IsMandatory, int Priority, ConditionMatch Match,
    IReadOnlyList<RuleCondition> Conditions, IReadOnlyList<WorkflowStep> Steps, bool OptedOut, bool CanEdit, int Pending, DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record GetWorkflowDefinitionsQuery : IQuery<IReadOnlyList<WorkflowDefinitionDto>>;

internal sealed class GetWorkflowDefinitionsQueryHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority)
    : IQueryHandler<GetWorkflowDefinitionsQuery, IReadOnlyList<WorkflowDefinitionDto>>
{
    public async Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> Handle(GetWorkflowDefinitionsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var definitions = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.TenantId == null || d.TenantId == tenantId).ToListAsync(cancellationToken);
        var optedOut = (await db.AutomationOptOuts.AsNoTracking().Where(o => o.Kind == AutomationKind.Workflow).Select(o => o.DefinitionId).ToListAsync(cancellationToken)).ToHashSet();
        var pending = await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Status == WorkflowStatus.Pending)
            .GroupBy(i => i.DefinitionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var canGlobal = await authority.CanManageGlobalAsync(cancellationToken);

        return definitions
            .OrderBy(d => d.Document).ThenBy(d => d.IsGlobal ? d.IsMandatory ? 0 : 2 : 1).ThenBy(d => d.Priority).ThenBy(d => d.Name)
            .Select(d => ToDto(d, optedOut.Contains(d.Id), d.IsGlobal ? canGlobal : authority.CanManageCompany, pending.GetValueOrDefault(d.Id)))
            .ToList();
    }

    internal static WorkflowDefinitionDto ToDto(WorkflowDefinition d, bool optedOut, bool canEdit, int pending) => new(
        d.Id, d.IsGlobal, d.Name, d.Description, d.Document, d.IsEnabled, d.IsMandatory, d.Priority, d.Match, d.Conditions, d.Steps, optedOut, canEdit, pending,
        d.CreatedAt, d.UpdatedAt, d.UpdatedBy);
}

/// <summary>Creates (scope chosen once: <see cref="Global"/>) or updates a workflow.</summary>
public sealed record SaveWorkflowDefinitionCommand(Guid? Id, bool Global, WorkflowDefinitionData Data) : ICommand<Guid>;

internal sealed class SaveWorkflowDefinitionCommandHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority)
    : ICommandHandler<SaveWorkflowDefinitionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveWorkflowDefinitionCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? definition = null;
        if (command.Id is { } id)
        {
            var tenantId = tenant.TenantId;
            definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == id && (d.TenantId == null || d.TenantId == tenantId), cancellationToken);
            if (definition is null)
            {
                return AutomationErrors.NotFound;
            }
        }

        var global = definition?.IsGlobal ?? command.Global;
        if (global && !await authority.CanManageGlobalAsync(cancellationToken))
        {
            return AutomationErrors.GlobalOnly;
        }

        if (!global)
        {
            var roles = await authority.EnsureRolesAsync((command.Data.Steps ?? []).SelectMany(s => s.Approvers?.RoleIds ?? []), cancellationToken);
            if (roles.IsFailure)
            {
                return roles.Error;
            }
        }

        if (definition is null)
        {
            var created = WorkflowDefinition.Create(global ? null : tenant.TenantId, command.Data);
            if (created.IsFailure)
            {
                return created.Error;
            }

            definition = created.Value;
            db.WorkflowDefinitions.Add(definition);
        }
        else
        {
            var updated = definition.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return definition.Id;
    }
}

public sealed record DeleteWorkflowDefinitionCommand(Guid Id) : ICommand;

/// <summary>Requests in progress keep their copy of the steps and finish normally.</summary>
internal sealed class DeleteWorkflowDefinitionCommandHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority) : ICommandHandler<DeleteWorkflowDefinitionCommand>
{
    public async Task<Result> Handle(DeleteWorkflowDefinitionCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var definition = await db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == command.Id && (d.TenantId == null || d.TenantId == tenantId), cancellationToken);
        if (definition is null)
        {
            return AutomationErrors.NotFound;
        }

        if (definition.IsGlobal && !await authority.CanManageGlobalAsync(cancellationToken))
        {
            return AutomationErrors.GlobalOnly;
        }

        db.WorkflowDefinitions.Remove(definition);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Business rules ---------------------------------------------------------------------------------------

public sealed record BusinessRuleDto(
    Guid Id, bool IsGlobal, string Name, string? Description, AutomationDocument Document, IReadOnlyList<AutomationTrigger> Triggers, bool IsEnabled, bool IsMandatory,
    int Priority, ConditionMatch Match, IReadOnlyList<RuleCondition> Conditions, RuleEffect Effect, string Message, NotificationSeverity Severity, RuleRecipients Recipients,
    bool OptedOut, bool CanEdit, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record GetBusinessRulesQuery : IQuery<IReadOnlyList<BusinessRuleDto>>;

internal sealed class GetBusinessRulesQueryHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority)
    : IQueryHandler<GetBusinessRulesQuery, IReadOnlyList<BusinessRuleDto>>
{
    public async Task<Result<IReadOnlyList<BusinessRuleDto>>> Handle(GetBusinessRulesQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var rules = await db.BusinessRules.AsNoTracking().Where(r => r.TenantId == null || r.TenantId == tenantId).ToListAsync(cancellationToken);
        var optedOut = (await db.AutomationOptOuts.AsNoTracking().Where(o => o.Kind == AutomationKind.Rule).Select(o => o.DefinitionId).ToListAsync(cancellationToken)).ToHashSet();
        var canGlobal = await authority.CanManageGlobalAsync(cancellationToken);

        return rules
            .OrderBy(r => r.Document).ThenBy(r => r.IsGlobal ? 0 : 1).ThenBy(r => r.Priority).ThenBy(r => r.Name)
            .Select(r => new BusinessRuleDto(r.Id, r.IsGlobal, r.Name, r.Description, r.Document, r.Triggers, r.IsEnabled, r.IsMandatory, r.Priority, r.Match, r.Conditions,
                r.Effect, r.Message, r.Severity, r.Recipients, optedOut.Contains(r.Id), r.IsGlobal ? canGlobal : authority.CanManageCompany, r.CreatedAt, r.UpdatedAt, r.UpdatedBy))
            .ToList();
    }
}

public sealed record SaveBusinessRuleCommand(Guid? Id, bool Global, BusinessRuleData Data) : ICommand<Guid>;

internal sealed class SaveBusinessRuleCommandHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority) : ICommandHandler<SaveBusinessRuleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveBusinessRuleCommand command, CancellationToken cancellationToken)
    {
        BusinessRule? rule = null;
        if (command.Id is { } id)
        {
            var tenantId = tenant.TenantId;
            rule = await db.BusinessRules.FirstOrDefaultAsync(r => r.Id == id && (r.TenantId == null || r.TenantId == tenantId), cancellationToken);
            if (rule is null)
            {
                return AutomationErrors.NotFound;
            }
        }

        var global = rule?.IsGlobal ?? command.Global;
        if (global && !await authority.CanManageGlobalAsync(cancellationToken))
        {
            return AutomationErrors.GlobalOnly;
        }

        if (!global)
        {
            var roles = await authority.EnsureRolesAsync(command.Data.Recipients?.Audience?.RoleIds ?? [], cancellationToken);
            if (roles.IsFailure)
            {
                return roles.Error;
            }
        }

        if (rule is null)
        {
            var created = BusinessRule.Create(global ? null : tenant.TenantId, command.Data);
            if (created.IsFailure)
            {
                return created.Error;
            }

            rule = created.Value;
            db.BusinessRules.Add(rule);
        }
        else
        {
            var updated = rule.Update(command.Data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return rule.Id;
    }
}

public sealed record DeleteBusinessRuleCommand(Guid Id) : ICommand;

internal sealed class DeleteBusinessRuleCommandHandler(IApplicationDbContext db, ITenantContext tenant, AutomationAuthority authority) : ICommandHandler<DeleteBusinessRuleCommand>
{
    public async Task<Result> Handle(DeleteBusinessRuleCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId;
        var rule = await db.BusinessRules.FirstOrDefaultAsync(r => r.Id == command.Id && (r.TenantId == null || r.TenantId == tenantId), cancellationToken);
        if (rule is null)
        {
            return AutomationErrors.NotFound;
        }

        if (rule.IsGlobal && !await authority.CanManageGlobalAsync(cancellationToken))
        {
            return AutomationErrors.GlobalOnly;
        }

        db.BusinessRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Optional global definitions --------------------------------------------------------------------------

/// <summary>The company switches off (or back on) an optional global workflow or rule.</summary>
public sealed record SetAutomationOptOutCommand(AutomationKind Kind, Guid Id, bool OptOut) : ICommand;

internal sealed class SetAutomationOptOutCommandHandler(IApplicationDbContext db, IUserContext user, ITenantClock clock, IAuditLog audit) : ICommandHandler<SetAutomationOptOutCommand>
{
    public async Task<Result> Handle(SetAutomationOptOutCommand command, CancellationToken cancellationToken)
    {
        var definition = command.Kind == AutomationKind.Workflow
            ? await db.WorkflowDefinitions.AsNoTracking().Where(d => d.Id == command.Id).Select(d => new { d.TenantId, d.IsMandatory, d.Name }).FirstOrDefaultAsync(cancellationToken)
            : await db.BusinessRules.AsNoTracking().Where(r => r.Id == command.Id).Select(r => new { r.TenantId, r.IsMandatory, r.Name }).FirstOrDefaultAsync(cancellationToken);
        if (definition is null)
        {
            return AutomationErrors.NotFound;
        }

        if (definition.TenantId is not null)
        {
            return AutomationErrors.NotGlobal;
        }

        if (definition.IsMandatory)
        {
            return AutomationErrors.NotOptional;
        }

        var existing = await db.AutomationOptOuts.FirstOrDefaultAsync(o => o.Kind == command.Kind && o.DefinitionId == command.Id, cancellationToken);
        if (command.OptOut && existing is null)
        {
            db.AutomationOptOuts.Add(new AutomationOptOut(command.Kind, command.Id, user.DisplayName, clock.UtcNow));
        }
        else if (!command.OptOut && existing is not null)
        {
            db.AutomationOptOuts.Remove(existing);
        }

        var kind = command.Kind == AutomationKind.Workflow ? "flujo" : "regla";
        audit.Record(AuditAction.StatusChanged, command.Kind == AutomationKind.Workflow ? nameof(WorkflowDefinition) : nameof(BusinessRule), command.Id,
            $"{(command.OptOut ? "Desactivó para la empresa" : "Reactivó para la empresa")} el {kind} global '{definition.Name}'");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
