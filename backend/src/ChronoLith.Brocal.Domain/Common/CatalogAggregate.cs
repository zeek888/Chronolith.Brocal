using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Common;

/// <summary>
/// Master data of a tenant (customers, services, units, warehouses…): unique code per tenant, name,
/// notes and an active flag. Items with history are deactivated instead of deleted.
/// </summary>
public abstract class CatalogAggregate : TenantAggregateRoot
{
    public const int MaxNameLength = 200;
    public const int MaxNotesLength = 2000;

    protected CatalogAggregate()
    {
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Validates and assigns code, name and notes (shared by every create/update).</summary>
    protected Result SetIdentity(string code, string name, string? notes)
    {
        var normalizedCode = Codes.Normalize(code ?? string.Empty);
        if (!Codes.IsValid(normalizedCode))
        {
            return CatalogErrors.InvalidCode;
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return CatalogErrors.InvalidName;
        }

        if (notes is { Length: > MaxNotesLength })
        {
            return CatalogErrors.NotesTooLong;
        }

        Code = normalizedCode;
        Name = name.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        return Result.Success();
    }
}

public static class CatalogErrors
{
    public static readonly Error InvalidCode = Error.Validation(
        "Catalogs.InvalidCode", "La clave usa letras, números, punto, guion, guion bajo o '/' (máximo 40 caracteres).");

    public static readonly Error InvalidName = Error.Validation("Catalogs.InvalidName", "El nombre es obligatorio (máximo 200 caracteres).");

    public static readonly Error NotesTooLong = Error.Validation("Catalogs.NotesTooLong", "Las notas admiten hasta 2000 caracteres.");

    public static Error NotFound(string catalog) => Error.NotFound($"{catalog}.NotFound", "El registro no existe.");

    public static Error CodeAlreadyExists(string catalog, string code) =>
        Error.Conflict($"{catalog}.CodeAlreadyExists", $"Ya existe un registro con la clave '{code}'.");

    public static Error InUse(string catalog) =>
        Error.Conflict($"{catalog}.InUse", "El registro ya se usa en otros documentos: desactívalo en lugar de eliminarlo.");

    public static Error ReferenceNotFound(string catalog) =>
        Error.Validation($"{catalog}.ReferenceNotFound", "El registro relacionado no existe.");

    public static Error Inactive(string catalog) =>
        Error.Validation($"{catalog}.Inactive", "El registro relacionado está inactivo.");
}
