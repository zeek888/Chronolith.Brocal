using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ChronoLith.Brocal.Infrastructure.Tenancy;

/// <summary>
/// Separate unit of work (new DI scope, new DbContext and connection) with the company, membership and permissions
/// of the current one. The user comes from the same HTTP request, so audit columns keep the same author.
/// </summary>
internal sealed class IsolatedWriter(IServiceScopeFactory scopeFactory, ITenantContext current) : IIsolatedWriter
{
    public async Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (current.HasTenant)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(current.Tenant, current.Membership, current.IsPlatformAdmin, current.Permissions);
        }

        await work(scope.ServiceProvider, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().SaveChangesAsync(cancellationToken);
    }
}
