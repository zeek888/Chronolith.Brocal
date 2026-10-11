using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>Named set of permissions of a company (Oficina, Operador de campo, Cliente…).</summary>
public sealed class TenantRole : TenantAggregateRoot
{
    private List<string> _permissions = [];

    private TenantRole()
    {
    }

    private TenantRole(string name, string? description, string? templateKey)
    {
        Name = name;
        Description = description;
        TemplateKey = templateKey;
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Default role it was created from (<see cref="RoleTemplates"/>), if any.</summary>
    public string? TemplateKey { get; private set; }

    public IReadOnlyList<string> Permissions => _permissions;

    public static Result<TenantRole> Create(string name, string? description, IEnumerable<string> permissions, string? templateKey = null)
    {
        var role = new TenantRole(string.Empty, null, templateKey);
        var result = role.Update(name, description, permissions);

        return result.IsFailure ? result.Error : role;
    }

    public static TenantRole FromTemplate(RoleTemplates.Template template) =>
        Create(template.Name, template.Description, template.Permissions, template.Key).Value;

    public Result Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            return RoleErrors.InvalidName;
        }

        if (description is { Length: > 300 })
        {
            return RoleErrors.DescriptionTooLong;
        }

        var codes = permissions.Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var unknown = codes.FirstOrDefault(c => !BrocalModules.IsPermission(c));
        if (unknown is not null)
        {
            return RoleErrors.UnknownPermission(unknown);
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _permissions = codes.Order(StringComparer.Ordinal).ToList();

        return Result.Success();
    }
}

public static class RoleErrors
{
    public static readonly Error NotFound = Error.NotFound("Roles.NotFound", "El rol no existe.");

    public static readonly Error InvalidName = Error.Validation("Roles.InvalidName", "El nombre del rol es obligatorio (máximo 80 caracteres).");

    public static readonly Error DescriptionTooLong = Error.Validation("Roles.DescriptionTooLong", "La descripción admite hasta 300 caracteres.");

    public static readonly Error NameTaken = Error.Conflict("Roles.NameTaken", "Ya existe un rol con ese nombre.");

    public static readonly Error InUse = Error.Conflict("Roles.InUse", "El rol está asignado a usuarios: quítalo de ellos antes de eliminarlo.");

    public static Error UnknownPermission(string code) => Error.Validation("Roles.UnknownPermission", $"El permiso '{code}' no existe.");
}
