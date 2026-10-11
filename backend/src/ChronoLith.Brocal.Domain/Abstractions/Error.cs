namespace ChronoLith.Brocal.Domain.Abstractions;

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Forbidden = 4
}

/// <summary>
/// Describes why an operation failed. Errors are values (not exceptions) so that
/// expected business failures flow through <see cref="Result"/>.
/// </summary>
public record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}

public sealed record ValidationError(IReadOnlyList<Error> Errors)
    : Error("General.Validation", "Se encontraron uno o más errores de validación.", ErrorType.Validation);
