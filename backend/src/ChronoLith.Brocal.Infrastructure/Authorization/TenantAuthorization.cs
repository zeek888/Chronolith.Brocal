using System.Collections.Concurrent;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Authorization;

/// <summary>Policy names understood by <see cref="BrocalPolicyProvider"/>.</summary>
public static class BrocalPolicies
{
    /// <summary>The request works on a company (any member).</summary>
    public const string Tenant = "tenant";

    /// <summary>Platform administration (companies, quotas, modules).</summary>
    public const string PlatformAdmin = "platform-admin";

    /// <summary>Prefix of permission policies: <c>tenant:tickets.capture</c> or <c>tenant:tickets.read|tickets.readown</c> (any of).</summary>
    public const string PermissionPrefix = "tenant:";

    public static string Permission(params string[] anyOf) => PermissionPrefix + string.Join('|', anyOf);
}

public sealed class TenantPermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    /// <summary>Empty = only a company is required.</summary>
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

public sealed class PlatformAdminRequirement : IAuthorizationRequirement;

/// <summary>The module of the permission must be enabled for the company and the user must hold the permission.</summary>
internal sealed class TenantPermissionHandler(ITenantContext tenantContext) : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
    {
        if (!tenantContext.HasTenant)
        {
            return Task.CompletedTask;
        }

        if (requirement.AnyOf.Count == 0 ||
            requirement.AnyOf.Any(p => tenantContext.IsModuleEnabled(BrocalModules.ModuleOf(p)) && tenantContext.Has(p)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal sealed class PlatformAdminHandler(IPlatformAccessService platformAccess) : AuthorizationHandler<PlatformAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && (await platformAccess.GetAsync(CancellationToken.None)).CanManagePlatform)
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Builds the tenant/permission policies on demand instead of registering one per permission.</summary>
internal sealed class BrocalPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly ConcurrentDictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = await base.GetPolicyAsync(policyName);
        if (policy is not null)
        {
            return policy;
        }

        return policyName switch
        {
            BrocalPolicies.Tenant => _policies.GetOrAdd(policyName, _ => Build(new TenantPermissionRequirement([]))),
            BrocalPolicies.PlatformAdmin => _policies.GetOrAdd(policyName, _ => Build(new PlatformAdminRequirement())),
            _ when policyName.StartsWith(BrocalPolicies.PermissionPrefix, StringComparison.Ordinal) => _policies.GetOrAdd(policyName, name =>
                Build(new TenantPermissionRequirement(name[BrocalPolicies.PermissionPrefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries)))),
            _ => null
        };
    }

    private static AuthorizationPolicy Build(IAuthorizationRequirement requirement) =>
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(requirement).Build();
}
