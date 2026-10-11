using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Platform;

/// <summary>Integration with the ChronoLith Shell (module mode).</summary>
public sealed class ShellOptions
{
    public const string SectionName = "Shell";

    /// <summary>Shell API for server-to-server calls (e.g. <c>http://shell-api:8080</c>). Empty in standalone mode.</summary>
    public string? ApiUrl { get; init; } = "http://localhost:5080";

    public int PermissionsCacheSeconds { get; init; } = 120;

    /// <summary>Require the Shell permission <c>brocal.access</c> (otherwise any authenticated user may use Brocal).</summary>
    public bool RequireAccessPermission { get; init; } = true;
}

public sealed class PlatformUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Module mode: who may open Brocal and administer the platform comes from the Shell RBAC
/// (<c>GET /api/me/permissions?module=brocal</c> with the user's token, cached per user).
/// </summary>
internal sealed class ShellPlatformAccessService(
    HttpClient http,
    IMemoryCache cache,
    IHttpContextAccessor httpContext,
    IUserContext user,
    IOptions<KeycloakOptions> keycloak,
    IOptions<ShellOptions> shell,
    ILogger<ShellPlatformAccessService> logger) : IPlatformAccessService
{
    public async Task<PlatformAccess> GetAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return PlatformAccess.None;
        }

        if (keycloak.Value.PlatformAdminRoles.Any(user.IsInRole))
        {
            return new PlatformAccess(true, true);
        }

        return await cache.GetOrCreateAsync($"platform-access:{user.IdentityId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(shell.Value.PermissionsCacheSeconds);
            var permissions = await QueryAsync(cancellationToken);
            var manage = permissions.Contains(PlatformPermissions.ManagePlatform);
            var access = manage || permissions.Contains(PlatformPermissions.Access) || !shell.Value.RequireAccessPermission;
            return new PlatformAccess(access, manage);
        }) ?? PlatformAccess.None;
    }

    private async Task<HashSet<string>> QueryAsync(CancellationToken cancellationToken)
    {
        var header = httpContext.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !AuthenticationHeaderValue.TryParse(header, out var token))
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/me/permissions?module={PlatformPermissions.ModuleCode}");
        request.Headers.Authorization = token;

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<string[]>(cancellationToken) ?? []).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "No se pudieron consultar los permisos en el Shell de ChronoLith");
            throw new PlatformUnavailableException("La plataforma ChronoLith no responde. Intenta de nuevo en unos minutos.", ex);
        }
    }
}

/// <summary>
/// Standalone mode: any authenticated user of the realm may open Brocal (company memberships still gate the data);
/// the platform administrators are the realm roles configured in <see cref="KeycloakOptions.PlatformAdminRoles"/>.
/// </summary>
internal sealed class StandalonePlatformAccessService(IUserContext user, IOptions<KeycloakOptions> keycloak) : IPlatformAccessService
{
    public Task<PlatformAccess> GetAsync(CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return Task.FromResult(PlatformAccess.None);
        }

        var admin = keycloak.Value.PlatformAdminRoles.Any(user.IsInRole);
        return Task.FromResult(new PlatformAccess(true, admin));
    }
}

internal static class PlatformModes
{
    public static bool IsStandalone(TenancyOptions options) => string.Equals(options.Mode, "Standalone", StringComparison.OrdinalIgnoreCase);
}
