using System.Net.Mail;
using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

public enum MembershipStatus
{
    /// <summary>Invited by e-mail; it binds to the Keycloak account the first time that person signs in.</summary>
    Invited = 0,
    Active = 1,
    Disabled = 2
}

/// <summary>
/// A person's access to one company: Keycloak identity (bound on first sign-in through the e-mail),
/// roles of the company, and optional links to an employee record (field staff) or a customer (portal users).
/// One Keycloak account can belong to several companies.
/// </summary>
public sealed class TenantMembership : TenantAggregateRoot
{
    private List<Guid> _roleIds = [];

    private TenantMembership()
    {
    }

    private TenantMembership(string email, string fullName, bool isTenantAdmin)
    {
        Email = email;
        FullName = fullName;
        IsTenantAdmin = isTenantAdmin;
        InvitedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Keycloak subject (<c>sub</c>); null until the person signs in.</summary>
    public string? IdentityId { get; private set; }

    public string Email { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public MembershipStatus Status { get; private set; } = MembershipStatus.Invited;

    /// <summary>Company administrator: every permission of the company, regardless of roles.</summary>
    public bool IsTenantAdmin { get; private set; }

    public IReadOnlyList<Guid> RoleIds => _roleIds;

    /// <summary>Portal users see only the data of this customer.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Employee record of field staff ("my tickets", "my work orders", attendance, expenses).</summary>
    public Guid? EmployeeId { get; private set; }

    public DateTimeOffset? InvitedAt { get; private set; }

    public DateTimeOffset? LastAccessAt { get; private set; }

    public bool IsPortalUser => CustomerId is not null;

    public static Result<TenantMembership> Invite(string email, string fullName, bool isTenantAdmin, IEnumerable<Guid> roleIds)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null)
        {
            return MembershipErrors.InvalidEmail;
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 150)
        {
            return MembershipErrors.InvalidName;
        }

        var membership = new TenantMembership(normalized, fullName.Trim(), isTenantAdmin)
        {
            _roleIds = roleIds.Distinct().ToList()
        };

        return membership;
    }

    /// <summary>Links the invitation with the Keycloak account that signed in with the same e-mail.</summary>
    public Result Bind(string identityId, string? fullName, DateTimeOffset now)
    {
        if (IdentityId is not null && IdentityId != identityId)
        {
            return MembershipErrors.AlreadyBound;
        }

        IdentityId = identityId;
        if (Status == MembershipStatus.Invited)
        {
            Status = MembershipStatus.Active;
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            FullName = fullName.Trim();
        }

        LastAccessAt = now;
        return Result.Success();
    }

    public void Touch(DateTimeOffset now) => LastAccessAt = now;

    public Result Update(string fullName, bool isTenantAdmin, IEnumerable<Guid> roleIds, Guid? customerId, Guid? employeeId)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 150)
        {
            return MembershipErrors.InvalidName;
        }

        FullName = fullName.Trim();
        IsTenantAdmin = isTenantAdmin;
        _roleIds = roleIds.Distinct().ToList();
        CustomerId = customerId;
        EmployeeId = employeeId;

        return Result.Success();
    }

    public void LinkEmployee(Guid? employeeId) => EmployeeId = employeeId;

    public void Disable() => Status = MembershipStatus.Disabled;

    public void Enable() => Status = IdentityId is null ? MembershipStatus.Invited : MembershipStatus.Active;

    public void RemoveRole(Guid roleId) => _roleIds.Remove(roleId);

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return null;
        }

        var trimmed = email.Trim().ToLowerInvariant();
        return MailAddress.TryCreate(trimmed, out var address) && address.Address == trimmed ? trimmed : null;
    }
}

public static class MembershipErrors
{
    public static readonly Error NotFound = Error.NotFound("Memberships.NotFound", "El usuario no pertenece a esta empresa.");

    public static readonly Error InvalidEmail = Error.Validation("Memberships.InvalidEmail", "El correo electrónico no es válido.");

    public static readonly Error InvalidName = Error.Validation("Memberships.InvalidName", "El nombre es obligatorio (máximo 150 caracteres).");

    public static readonly Error AlreadyBound = Error.Conflict("Memberships.AlreadyBound", "La invitación ya está ligada a otra cuenta.");

    public static readonly Error EmailTaken = Error.Conflict("Memberships.EmailTaken", "Ese correo ya pertenece a un usuario de la empresa.");

    public static readonly Error NoMembership = Error.Forbidden(
        "Memberships.None", "Tu cuenta no pertenece a ninguna empresa en Brocal. Pide al administrador de tu empresa que te invite.");

    public static readonly Error Disabled = Error.Forbidden("Memberships.Disabled", "Tu acceso a esta empresa está desactivado.");

    public static readonly Error TenantRequired = Error.Validation(
        "Memberships.TenantRequired", "Selecciona la empresa con la que vas a trabajar (encabezado X-Tenant).");

    public static readonly Error CannotDisableSelf = Error.Failure("Memberships.CannotDisableSelf", "No puedes desactivar tu propio acceso.");
}
