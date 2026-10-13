using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Application.Abstractions.Behaviors;

/// <summary>Decorator that runs FluentValidation validators before a command reaches its handler.</summary>
internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await Validation.ValidateAsync(command, validators, cancellationToken);

            return error is null
                ? await innerHandler.Handle(command, cancellationToken)
                : Result.Failure<TResponse>(error);
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        IEnumerable<IValidator<TCommand>> validators)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var error = await Validation.ValidateAsync(command, validators, cancellationToken);

            return error is null
                ? await innerHandler.Handle(command, cancellationToken)
                : Result.Failure(error);
        }
    }
}

/// <summary>Decorator that logs every command/query and its business failures.</summary>
internal static class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> innerHandler,
        ILoggerFactory loggerFactory)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await innerHandler.Handle(command, cancellationToken);
            Log(loggerFactory, typeof(TCommand), result, LogLevel.Information);

            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> innerHandler,
        ILoggerFactory loggerFactory)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await innerHandler.Handle(command, cancellationToken);
            Log(loggerFactory, typeof(TCommand), result, LogLevel.Information);

            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> innerHandler,
        ILoggerFactory loggerFactory)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var result = await innerHandler.Handle(query, cancellationToken);
            Log(loggerFactory, typeof(TQuery), result, LogLevel.Debug);

            return result;
        }
    }

    private static void Log(ILoggerFactory loggerFactory, Type requestType, Result result, LogLevel successLevel)
    {
        var logger = loggerFactory.CreateLogger(requestType.FullName ?? requestType.Name);

        if (result.IsSuccess)
        {
            logger.Log(successLevel, "{Request} completado", requestType.Name);
        }
        else if (result.Error is ApprovalPendingError)
        {
            // Held by an approval workflow: an expected outcome, not a failure.
            logger.LogInformation("{Request} en espera de autorización: {ErrorDescription}", requestType.Name, result.Error.Description);
        }
        else
        {
            logger.LogWarning("{Request} falló: {ErrorCode} - {ErrorDescription}", requestType.Name, result.Error.Code, result.Error.Description);
        }
    }
}

/// <summary>Runs FluentValidation validators and turns their failures into a <see cref="ValidationError"/>.</summary>
public static class Validation
{
    public static async Task<ValidationError?> ValidateAsync<T>(T instance, IEnumerable<IValidator<T>> validators, CancellationToken cancellationToken)
    {
        var validatorList = validators.ToArray();
        if (validatorList.Length == 0)
        {
            return null;
        }

        var context = new ValidationContext<T>(instance);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
            .ToList();

        return errors.Count == 0 ? null : new ValidationError(errors);
    }
}
