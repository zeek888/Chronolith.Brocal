using System.Collections.Concurrent;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.Infrastructure.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>Observer pattern: resolves every <see cref="IDomainEventHandler{T}"/> registered for each event.</summary>
internal sealed class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypes = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = HandlerTypes.GetOrAdd(domainEvent.GetType(), type => typeof(IDomainEventHandler<>).MakeGenericType(type));
            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                var task = (Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!
                    .Invoke(handler, [domainEvent, cancellationToken])!;
                await task;
            }
        }
    }
}
