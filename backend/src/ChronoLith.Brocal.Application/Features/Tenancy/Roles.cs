using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tenancy;

// Roles of the current company (access.roles).

public sealed record RoleDto(Guid Id, string Name, string? Description, string? TemplateKey, IReadOnlyList<string> Permissions, int Members);

public sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

internal sealed class GetRolesQueryHandler(IApplicationDbContext db) : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(GetRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var memberRoles = await db.Memberships.AsNoTracking().Where(m => m.Status != MembershipStatus.Disabled).Select(m => m.RoleIds).ToListAsync(cancellationToken);

        return roles.Select(r => new RoleDto(r.Id, r.Name, r.Description, r.TemplateKey, r.Permissions, memberRoles.Count(ids => ids.Contains(r.Id)))).ToList();
    }
}

public sealed record SaveRoleCommand(Guid? Id, string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<Guid>;

internal sealed class SaveRoleCommandValidator : AbstractValidator<SaveRoleCommand>
{
    public SaveRoleCommandValidator()
    {
        RuleFor(c => c.Name).RequiredName(80);
        RuleFor(c => c.Description).OptionalText(300);
    }
}

internal sealed class SaveRoleCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveRoleCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != command.Id, cancellationToken))
        {
            return RoleErrors.NameTaken;
        }

        if (command.Id is null)
        {
            var created = TenantRole.Create(name, command.Description, command.Permissions);
            if (created.IsFailure)
            {
                return created.Error;
            }

            db.Roles.Add(created.Value);
            await db.SaveChangesAsync(cancellationToken);
            return created.Value.Id;
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var result = role.Update(name, command.Description, command.Permissions);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return role.Id;
    }
}

public sealed record DeleteRoleCommand(Guid Id) : ICommand;

internal sealed class DeleteRoleCommandHandler(IApplicationDbContext db, IAuditLog audit) : ICommandHandler<DeleteRoleCommand>
{
    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        var assignments = await db.Memberships.Select(m => m.RoleIds).ToListAsync(cancellationToken);
        if (assignments.Any(ids => ids.Contains(role.Id)))
        {
            return RoleErrors.InUse;
        }

        var rules = await db.NotificationRules.ToListAsync(cancellationToken);
        foreach (var rule in rules.Where(r => r.RoleIds.Contains(role.Id)))
        {
            rule.RemoveRole(role.Id);
        }

        db.Roles.Remove(role);
        audit.Record(AuditAction.Deleted, nameof(TenantRole), role.Id, role.Name);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
