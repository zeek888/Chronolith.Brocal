using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChronoLith.Brocal.Infrastructure.Authentication;

public static class KeycloakClaims
{
    public const string Subject = "sub";
    public const string PreferredUsername = "preferred_username";
    public const string Email = "email";
    public const string EmailVerified = "email_verified";
    public const string Name = "name";
    public const string GivenName = "given_name";
    public const string FamilyName = "family_name";
    public const string RealmAccess = "realm_access";
    public const string Role = "role";
}

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Public base URL seen by browsers; defines the token issuer (e.g. <c>http://localhost:8080</c>).</summary>
    [Required]
    public string AuthServerUrl { get; init; } = "http://localhost:8080";

    /// <summary>Optional in-network URL for back-channel calls (e.g. <c>http://keycloak:8080</c> in Docker).</summary>
    public string? InternalAuthServerUrl { get; init; }

    [Required]
    public string Realm { get; init; } = "chronolith";

    /// <summary>Audiences accepted (ChronoLith APIs use <c>chronolith-api</c>).</summary>
    public string[] Audiences { get; init; } = ["chronolith-api"];

    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>Realm roles that grant platform administration (every company, quotas and modules).</summary>
    public string[] PlatformAdminRoles { get; init; } = ["chronolith-admin", "brocal-platform-admin"];

    /// <summary>Confidential client of the module (manifest registration and optional user provisioning).</summary>
    public string ClientId { get; init; } = "chronolith-brocal-api";

    public string? ClientSecret { get; init; }

    public string Issuer => $"{AuthServerUrl.TrimEnd('/')}/realms/{Realm}";

    public string BackchannelRealmUrl => $"{BackchannelBaseUrl}/realms/{Realm}";

    public string AdminApiUrl => $"{BackchannelBaseUrl}/admin/realms/{Realm}/";

    private string BackchannelBaseUrl => (string.IsNullOrWhiteSpace(InternalAuthServerUrl) ? AuthServerUrl : InternalAuthServerUrl).TrimEnd('/');
}

/// <summary>
/// Validates Keycloak access tokens. Metadata/JWKS are fetched through the back-channel URL while the issuer stays
/// the public URL, so the same token works for browsers and containers.
/// </summary>
internal sealed class JwtBearerOptionsSetup(IOptions<KeycloakOptions> keycloakOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        var keycloak = keycloakOptions.Value;

        options.Authority = keycloak.Issuer;
        options.MetadataAddress = $"{keycloak.BackchannelRealmUrl}/.well-known/openid-configuration";
        options.RequireHttpsMetadata = keycloak.RequireHttpsMetadata;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = keycloak.Issuer,
            ValidAudiences = keycloak.Audiences,
            NameClaimType = KeycloakClaims.PreferredUsername,
            RoleClaimType = KeycloakClaims.Role,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = AddRealmRoles };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    /// <summary>Flattens <c>realm_access.roles</c> into role claims so <c>IsInRole</c> works.</summary>
    private static Task AddRealmRoles(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity ||
            identity.FindFirst(KeycloakClaims.RealmAccess)?.Value is not { } realmAccess)
        {
            return Task.CompletedTask;
        }

        using var document = JsonDocument.Parse(realmAccess);
        if (document.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
        {
            foreach (var role in roles.EnumerateArray())
            {
                if (role.GetString() is { Length: > 0 } value)
                {
                    identity.AddClaim(new Claim(KeycloakClaims.Role, value));
                }
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class UserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string IdentityId =>
        Principal?.FindFirstValue(KeycloakClaims.Subject)
        ?? throw new InvalidOperationException("No hay un usuario autenticado o el token no contiene el claim 'sub'.");

    public string Username => Principal?.FindFirstValue(KeycloakClaims.PreferredUsername) ?? IdentityId;

    public string? Email => Principal?.FindFirstValue(KeycloakClaims.Email);

    public bool EmailVerified => string.Equals(Principal?.FindFirstValue(KeycloakClaims.EmailVerified), "true", StringComparison.OrdinalIgnoreCase);

    public string? FullName
    {
        get
        {
            var name = Principal?.FindFirstValue(KeycloakClaims.Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            var full = $"{Principal?.FindFirstValue(KeycloakClaims.GivenName)} {Principal?.FindFirstValue(KeycloakClaims.FamilyName)}".Trim();
            return full.Length == 0 ? null : full;
        }
    }

    public string DisplayName => IsAuthenticated ? FullName ?? Username : "system";

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? httpContextAccessor.HttpContext?.TraceIdentifier;
}
