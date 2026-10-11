using ChronoLith.Brocal.Domain.Common;
using FluentValidation;

namespace ChronoLith.Brocal.Application.Common;

/// <summary>Reusable FluentValidation rules (messages in Spanish, shown in the forms).</summary>
public static class ValidationRules
{
    public static IRuleBuilderOptions<T, string> BusinessCode<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("La clave es obligatoria.")
            .Must(code => Codes.IsValid(Codes.Normalize(code)))
            .WithMessage("Usa letras, números, punto, guion, guion bajo o '/' (máximo 40 caracteres).");

    public static IRuleBuilderOptions<T, string> RequiredName<T>(this IRuleBuilder<T, string> rule, int maxLength = 200) =>
        rule.NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(maxLength).WithMessage($"Máximo {maxLength} caracteres.");

    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.MaximumLength(maxLength).WithMessage($"Máximo {maxLength} caracteres.");

    public static IRuleBuilderOptions<T, string?> Rfc<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(rfc => string.IsNullOrWhiteSpace(rfc) || MexicanIds.IsValidRfc(MexicanIds.NormalizeRfc(rfc)))
            .WithMessage("El RFC no tiene un formato válido.");

    public static IRuleBuilderOptions<T, string?> PostalCode<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(cp => string.IsNullOrWhiteSpace(cp) || MexicanIds.IsValidPostalCode(cp))
            .WithMessage("El código postal debe tener 5 dígitos.");

    public static IRuleBuilderOptions<T, string?> Email<T>(this IRuleBuilder<T, string?> rule) =>
        rule.EmailAddress().WithMessage("El correo electrónico no es válido.");

    public static IRuleBuilderOptions<T, string?> Color<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(c => c is null || HexColor.IsValid(c)).WithMessage("Usa un color #RRGGBB.");

    public static IRuleBuilderOptions<T, Guid> RequiredId<T>(this IRuleBuilder<T, Guid> rule, string what) =>
        rule.NotEmpty().WithMessage($"Selecciona {what}.");

    public static IRuleBuilderOptions<T, decimal> NonNegative<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0).WithMessage("No puede ser negativo.");
}
