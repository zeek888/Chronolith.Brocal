using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Application.Abstractions.Messaging;

// CQRS contracts. Handlers are resolved directly from DI by the endpoints and wrapped by
// decorators (validation, logging), so no mediator library is needed.

public interface ICommand;

public interface ICommand<TResponse>;

public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Reacts to a domain event after the unit of work has been committed.</summary>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    Task Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
}
