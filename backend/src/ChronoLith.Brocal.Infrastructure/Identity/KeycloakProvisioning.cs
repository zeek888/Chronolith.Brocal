using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Identity;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Lets company administrators create Keycloak accounts when inviting users. The client of the module needs the
    /// realm-management roles manage-users, view-users and query-users.
    /// </summary>
    public bool ProvisionUsers { get; init; }
}

/// <summary>Client credentials of a service account: where to ask the token and as whom.</summary>
internal sealed record ClientCredentials(string TokenUrl, string ClientId, string ClientSecret);

/// <summary>Client-credentials token of the module's service account, cached until shortly before it expires.</summary>
internal class ServiceAccountTokenProvider(IHttpClientFactory httpClientFactory, IOptions<KeycloakOptions> keycloakOptions, TimeProvider timeProvider) : IDisposable
{
    public const string HttpClientName = "keycloak-token";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    /// <summary>The module's own account (<c>Keycloak:ClientId</c>) in the realm the API validates tokens against.</summary>
    protected virtual ClientCredentials Credentials
    {
        get
        {
            var keycloak = keycloakOptions.Value;
            return new ClientCredentials($"{keycloak.BackchannelRealmUrl}/protocol/openid-connect/token", keycloak.ClientId, keycloak.ClientSecret ?? string.Empty);
        }
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && timeProvider.GetUtcNow() < _expiresAt)
        {
            return _accessToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && timeProvider.GetUtcNow() < _expiresAt)
            {
                return _accessToken;
            }

            var credentials = Credentials;
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret
            });

            using var response = await client.PostAsync(credentials.TokenUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Keycloak respondió {(int)response.StatusCode} al pedir el token de '{credentials.ClientId}'.");
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Keycloak devolvió una respuesta de token vacía.");

            _accessToken = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(10, token.ExpiresIn - 30));
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _accessToken = null;

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>
/// Service account that talks to the Shell of ChronoLith (change feed, writes to the master). In module mode it is
/// the module's own account; standalone, the one configured in <c>Shell:ClientId</c> for the realm <c>chronolith</c>.
/// </summary>
internal sealed class ShellServiceAccountTokenProvider(IHttpClientFactory httpClientFactory, IOptions<KeycloakOptions> keycloakOptions, IOptions<Platform.ShellOptions> shellOptions, TimeProvider timeProvider)
    : ServiceAccountTokenProvider(httpClientFactory, keycloakOptions, timeProvider)
{
    protected override ClientCredentials Credentials
    {
        get
        {
            var shell = shellOptions.Value;
            return shell.HasOwnServiceAccount ? new ClientCredentials(shell.ServiceAccountTokenUrl, shell.ClientId!, shell.ClientSecret!) : base.Credentials;
        }
    }
}

internal sealed class ServiceAccountTokenHandler(ServiceAccountTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenProvider.GetTokenAsync(cancellationToken));
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.Invalidate();
        }

        return response;
    }
}

/// <summary>Keycloak Admin REST API (<c>/admin/realms/{realm}/users</c>) to find or create accounts for invitations.</summary>
internal sealed class KeycloakProvisioning(HttpClient http, IOptions<IdentityOptions> options, ILogger<KeycloakProvisioning> logger) : IIdentityProvisioning
{
    public bool IsEnabled => options.Value.ProvisionUsers;

    public async Task<IdentityUser?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        try
        {
            var users = await http.GetFromJsonAsync<KeycloakUser[]>($"users?email={Uri.EscapeDataString(email)}&exact=true", cancellationToken) ?? [];
            return users.Select(u => new IdentityUser(u.Id, u.Username, u.Email, u.FirstName, u.LastName, u.Enabled)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "No se pudo consultar Keycloak");
            return null;
        }
    }

    public async Task<Result<IdentityUser>> CreateAsync(string email, string firstName, string lastName, string temporaryPassword, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Error.Failure("Identity.Disabled", "La creación de cuentas en Keycloak no está habilitada.");
        }

        var representation = new
        {
            username = email,
            email,
            firstName,
            lastName,
            enabled = true,
            emailVerified = true,
            credentials = new[] { new { type = "password", value = temporaryPassword, temporary = true } }
        };

        try
        {
            using var response = await http.PostAsJsonAsync("users", representation, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return Error.Conflict("Identity.UserExists", "Ya existe una cuenta con ese correo en Keycloak.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Keycloak rechazó la cuenta {Email}: {Status} {Body}", email, (int)response.StatusCode, body);
                return Error.Failure("Identity.Rejected", "Keycloak rechazó la cuenta (revisa la política de contraseñas).");
            }

            var id = response.Headers.Location?.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;
            return new IdentityUser(id, email, email, firstName, lastName, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Keycloak Admin API no disponible");
            return Error.Failure("Identity.Unavailable", "Keycloak no responde.");
        }
    }

    private sealed record KeycloakUser(string Id, string Username, string? Email, string? FirstName, string? LastName, bool Enabled);
}
