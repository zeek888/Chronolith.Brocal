using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Behaviors;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Notifications;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Application.Features.Tickets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)).Where(IsConcreteHandler), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)).Where(IsConcreteHandler), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)).Where(IsConcreteHandler), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(c => c.AssignableTo(typeof(ICatalogSpec<,,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        // Decorator pattern: the last registered decorator is the outermost one.
        // Request flow: Logging -> Validation -> Handler.
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));

        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped(typeof(CatalogService<,,>));
        services.AddScoped<ITenantClock, TenantClock>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();
        services.AddScoped<IQuotaGuard, QuotaGuard>();
        services.AddScoped<IDailyScanService, DailyScanService>();
        services.AddScoped<IDocumentBrandingProvider, DocumentBrandingProvider>();
        services.AddScoped<TicketPdfService>();
        services.AddScoped<TicketClosing>();
        services.AddScoped<Features.Dispatch.DispatchConflicts>();
        services.AddScoped<Features.Dispatch.WorkOrderNotifier>();
        services.AddScoped<Features.Inventory.RegisterMovementCommandHandler>();
        services.AddScoped<Features.Invoicing.InvoiceStamping>();
        services.AddScoped<Features.Invoicing.InvoicePdfService>();
        services.AddScoped<Features.Integrations.IApiKeyAuthenticator, Features.Integrations.ApiKeyAuthenticator>();

        return services;
    }

    // Decorators are nested generic types; only top-level closed handlers are registered.
    private static bool IsConcreteHandler(Type type) => type is { IsGenericTypeDefinition: false, DeclaringType: null };
}
