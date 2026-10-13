using System.Text.Json;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;

namespace ChronoLith.Brocal.Api.Extensions;

/// <summary>Translates <see cref="Result"/> failures into RFC 9457 problem details.</summary>
public static class ResultExtensions
{
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.ToProblem();

    public static IResult ToNoContent(this Result result) =>
        result.IsSuccess ? Results.NoContent() : result.ToProblem();

    public static IResult ToCreated(this Result<Guid> result, string basePath) =>
        result.IsSuccess ? Results.Created($"{basePath}/{result.Value}", new { id = result.Value }) : result.ToProblem();

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? Results.Created(location(result.Value), result.Value) : result.ToProblem();

    public static IResult ToFile(this Result<FileContent> result, bool inline = false) =>
        result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, inline ? null : result.Value.FileName)
            : result.ToProblem();

    public static IResult ToProblem(this Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("Un resultado exitoso no se puede convertir en un problema.");
        }

        var error = result.Error;

        // An action held by an approval workflow is not an error: the request was registered (202).
        if (error is ApprovalPendingError pending)
        {
            return Results.Json(new ApprovalPendingResponse(true, pending.InstanceId, pending.Description), statusCode: StatusCodes.Status202Accepted);
        }

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error is ValidationError validation)
        {
            extensions["errors"] = validation.Errors
                .GroupBy(e => ToCamelCasePath(e.Code))
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        }

        return Results.Problem(
            title: GetTitle(error.Type),
            detail: error.Description,
            statusCode: GetStatusCode(error.Type),
            extensions: extensions);
    }

    private static int GetStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status422UnprocessableEntity
    };

    private static string GetTitle(ErrorType type) => type switch
    {
        ErrorType.Validation => "Datos inválidos",
        ErrorType.NotFound => "Recurso no encontrado",
        ErrorType.Conflict => "Conflicto",
        ErrorType.Forbidden => "Acceso denegado",
        _ => "Regla de negocio incumplida"
    };

    // "Data.CustomerId" -> "data.customerId"
    private static string ToCamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

/// <summary>Body of a <c>202 Accepted</c>: the action waits for the approval request <see cref="InstanceId"/>.</summary>
public sealed record ApprovalPendingResponse(bool ApprovalPending, Guid InstanceId, string Message);
