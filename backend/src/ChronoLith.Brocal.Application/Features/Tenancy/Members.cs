using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tenancy;

// Users of the current company (access.users): invitations, roles, links to employee/customer.

public sealed record MemberDto(
    Guid Id,
    string Email,
    string FullName,
    MembershipStatus Status,
    bool IsTenantAdmin,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid? CustomerId,
    string? CustomerName,
    bool IsBound,
    DateTimeOffset? InvitedAt,
    DateTimeOffset? LastAccessAt);

public sealed record SearchMembersQuery(string? Search, MembershipStatus? Status, bool? PortalUsers, Guid? RoleId, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<MemberDto>>;

internal sealed class SearchMembersQueryHandler(IApplicationDbContext db) : IQueryHandler<SearchMembersQuery, PagedResult<MemberDto>>
{
    public async Task<Result<PagedResult<MemberDto>>> Handle(SearchMembersQuery query, CancellationToken cancellationToken)
    {
        var request = new PageRequest(query.Search, query.Page, query.PageSize);
        var members = db.Memberships.AsNoTracking();
        if (request.Term is { } term)
        {
            members = members.Where(m => m.Email.Contains(term) || m.FullName.ToLower().Contains(term));
        }

        members = members
            .WhereIf(query.Status is not null, m => m.Status == query.Status)
            .WhereIf(query.PortalUsers == true, m => m.CustomerId != null)
            .WhereIf(query.PortalUsers == false, m => m.CustomerId == null);

        var all = await members.OrderBy(m => m.FullName).ToListAsync(cancellationToken);
        if (query.RoleId is { } roleId)
        {
            all = all.Where(m => m.RoleIds.Contains(roleId)).ToList();
        }

        var page = all.Skip((request.NormalizedPage - 1) * request.NormalizedPageSize).Take(request.NormalizedPageSize).ToList();
        var items = await MemberProjection.ToDtosAsync(db, page, cancellationToken);
        return new PagedResult<MemberDto>(items, request.NormalizedPage, request.NormalizedPageSize, all.Count);
    }
}

internal static class MemberProjection
{
    public static async Task<List<MemberDto>> ToDtosAsync(IApplicationDbContext db, IReadOnlyList<TenantMembership> members, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var employeeIds = members.Select(m => m.EmployeeId).OfType<Guid>().ToList();
        var customerIds = members.Select(m => m.CustomerId).OfType<Guid>().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return members.Select(m => new MemberDto(
            m.Id,
            m.Email,
            m.FullName,
            m.Status,
            m.IsTenantAdmin,
            m.RoleIds,
            m.RoleIds.Select(r => roles.GetValueOrDefault(r)).OfType<string>().Order().ToList(),
            m.EmployeeId,
            m.EmployeeId is { } e ? employees.GetValueOrDefault(e) : null,
            m.CustomerId,
            m.CustomerId is { } c ? customers.GetValueOrDefault(c) : null,
            m.IdentityId is not null,
            m.InvitedAt,
            m.LastAccessAt)).ToList();
    }
}

public sealed record InviteMemberCommand(
    string Email,
    string FullName,
    bool IsTenantAdmin,
    IReadOnlyList<Guid> RoleIds,
    Guid? EmployeeId,
    Guid? CustomerId,
    bool CreateAccount,
    string? TemporaryPassword) : ICommand<InviteMemberResult>;

public sealed record InviteMemberResult(Guid Id, bool AccountCreated, bool AccountExisted);

internal sealed class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().WithMessage("El correo electrónico no es válido.");
        RuleFor(c => c.FullName).RequiredName(150);
        RuleFor(c => c.TemporaryPassword).NotEmpty().MinimumLength(8).When(c => c.CreateAccount)
            .WithMessage("La contraseña temporal requiere al menos 8 caracteres.");
        RuleFor(c => c).Must(c => !(c.IsTenantAdmin && c.CustomerId is not null))
            .WithMessage("Un usuario del portal del cliente no puede ser administrador de la empresa.");
    }
}

internal sealed class InviteMemberCommandHandler(
    IApplicationDbContext db,
    IQuotaGuard quotas,
    IIdentityProvisioning identity,
    IAuditLog audit) : ICommandHandler<InviteMemberCommand, InviteMemberResult>
{
    public async Task<Result<InviteMemberResult>> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var email = TenantMembership.NormalizeEmail(command.Email);
        if (email is null)
        {
            return MembershipErrors.InvalidEmail;
        }

        if (await db.Memberships.AnyAsync(m => m.Email == email, cancellationToken))
        {
            return MembershipErrors.EmailTaken;
        }

        var quota = await quotas.EnsureCanAddUserAsync(command.CustomerId is not null, cancellationToken);
        if (quota.IsFailure)
        {
            return quota.Error;
        }

        var references = await MemberReferences.ValidateAsync(db, command.RoleIds, command.EmployeeId, command.CustomerId, cancellationToken);
        if (references.IsFailure)
        {
            return references.Error;
        }

        var invited = TenantMembership.Invite(email, command.FullName, command.IsTenantAdmin, command.RoleIds);
        if (invited.IsFailure)
        {
            return invited.Error;
        }

        var membership = invited.Value;
        membership.Update(membership.FullName, command.IsTenantAdmin, command.RoleIds, command.CustomerId, command.EmployeeId);

        var accountCreated = false;
        var accountExisted = false;
        if (identity.IsEnabled)
        {
            var existing = await identity.FindByEmailAsync(email, cancellationToken);
            accountExisted = existing is not null;
            if (existing is null && command.CreateAccount)
            {
                var (first, last) = CreateTenantCommandHandler.SplitName(command.FullName);
                var account = await identity.CreateAsync(email, first, last, command.TemporaryPassword!, cancellationToken);
                if (account.IsFailure)
                {
                    return account.Error;
                }

                accountCreated = true;
            }
        }

        db.Memberships.Add(membership);
        audit.Record(AuditAction.Created, nameof(TenantMembership), membership.Id, email);
        await db.SaveChangesAsync(cancellationToken);
        return new InviteMemberResult(membership.Id, accountCreated, accountExisted);
    }
}

public sealed record UpdateMemberCommand(Guid Id, string FullName, bool IsTenantAdmin, IReadOnlyList<Guid> RoleIds, Guid? EmployeeId, Guid? CustomerId) : ICommand;

internal sealed class UpdateMemberCommandHandler(IApplicationDbContext db, ITenantContext tenantContext, IQuotaGuard quotas) : ICommandHandler<UpdateMemberCommand>
{
    public async Task<Result> Handle(UpdateMemberCommand command, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (membership is null)
        {
            return MembershipErrors.NotFound;
        }

        if (membership.Id == tenantContext.Membership?.Id && membership.IsTenantAdmin && !command.IsTenantAdmin)
        {
            return Error.Failure("Memberships.CannotDemoteSelf", "No puedes quitarte a ti mismo el rol de administrador.");
        }

        if (command.IsTenantAdmin && command.CustomerId is not null)
        {
            return Error.Validation("Memberships.PortalAdmin", "Un usuario del portal del cliente no puede ser administrador de la empresa.");
        }

        if (membership.CustomerId is null != (command.CustomerId is null))
        {
            var quota = await quotas.EnsureCanAddUserAsync(command.CustomerId is not null, cancellationToken);
            if (quota.IsFailure)
            {
                return quota;
            }
        }

        var references = await MemberReferences.ValidateAsync(db, command.RoleIds, command.EmployeeId, command.CustomerId, cancellationToken);
        if (references.IsFailure)
        {
            return references;
        }

        var result = membership.Update(command.FullName, command.IsTenantAdmin, command.RoleIds, command.CustomerId, command.EmployeeId);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public sealed record SetMemberStatusCommand(Guid Id, bool Enabled) : ICommand;

internal sealed class SetMemberStatusCommandHandler(IApplicationDbContext db, ITenantContext tenantContext, IQuotaGuard quotas, IAuditLog audit)
    : ICommandHandler<SetMemberStatusCommand>
{
    public async Task<Result> Handle(SetMemberStatusCommand command, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (membership is null)
        {
            return MembershipErrors.NotFound;
        }

        if (!command.Enabled && membership.Id == tenantContext.Membership?.Id)
        {
            return MembershipErrors.CannotDisableSelf;
        }

        if (command.Enabled && membership.Status == MembershipStatus.Disabled)
        {
            var quota = await quotas.EnsureCanAddUserAsync(membership.IsPortalUser, cancellationToken);
            if (quota.IsFailure)
            {
                return quota;
            }

            membership.Enable();
        }
        else if (!command.Enabled)
        {
            membership.Disable();
        }

        audit.Record(AuditAction.StatusChanged, nameof(TenantMembership), membership.Id, $"{membership.Email} → {membership.Status}");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record RemoveMemberCommand(Guid Id) : ICommand;

internal sealed class RemoveMemberCommandHandler(IApplicationDbContext db, ITenantContext tenantContext, IAuditLog audit) : ICommandHandler<RemoveMemberCommand>
{
    public async Task<Result> Handle(RemoveMemberCommand command, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
        if (membership is null)
        {
            return MembershipErrors.NotFound;
        }

        if (membership.Id == tenantContext.Membership?.Id)
        {
            return MembershipErrors.CannotDisableSelf;
        }

        db.Memberships.Remove(membership);
        audit.Record(AuditAction.Deleted, nameof(TenantMembership), membership.Id, membership.Email);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class MemberReferences
{
    public static async Task<Result> ValidateAsync(IApplicationDbContext db, IReadOnlyList<Guid> roleIds, Guid? employeeId, Guid? customerId, CancellationToken cancellationToken)
    {
        var distinctRoles = roleIds.Distinct().ToList();
        if (distinctRoles.Count > 0 && await db.Roles.CountAsync(r => distinctRoles.Contains(r.Id), cancellationToken) != distinctRoles.Count)
        {
            return RoleErrors.NotFound;
        }

        if (employeeId is not null && !await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return Domain.Common.CatalogErrors.ReferenceNotFound("Employees");
        }

        if (customerId is not null && !await db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return Domain.Common.CatalogErrors.ReferenceNotFound("Customers");
        }

        return Result.Success();
    }
}
