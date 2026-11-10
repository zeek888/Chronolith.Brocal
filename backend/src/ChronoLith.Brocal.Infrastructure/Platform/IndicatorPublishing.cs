using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Indicators;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Authentication;
using ChronoLith.Brocal.Infrastructure.Identity;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Platform;

/// <summary>Indicators published to the ChronoLith Shell (<c>ChronoLith/docs/indicators.md</c>).</summary>
public sealed class IndicatorOptions
{
    public const string SectionName = "Indicators";

    /// <summary>Publishes the indicators when Brocal is connected to a Shell (the same connection as the shared catalogs).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Standalone mode: public <c>https://</c> URL of Brocal (e.g. <c>https://brocal.chronolith.com.mx</c>) for the
    /// <c>link</c> of each value. Empty, or not https, publishes values without link. In module mode the link is the
    /// route of the module in the Shell (<c>/brocal/…</c>).
    /// </summary>
    public string? PublicUrl { get; init; }

    /// <summary>How often the outbox is processed (seconds).</summary>
    public int DispatchSeconds { get; init; } = 60;
}

/// <summary>Indicators are published with the connection of the shared catalogs (Shell API and service account) unless disabled.</summary>
internal static class IndicatorAvailability
{
    public static bool IsEnabled(IndicatorOptions indicators, ShellOptions shell, KeycloakOptions keycloak, TenancyOptions tenancy) =>
        indicators.Enabled && SharedCatalogAvailability.IsEnabled(shell, keycloak, PlatformModes.IsStandalone(tenancy));

    public static IndicatorPublishingSettings Settings(IndicatorOptions indicators, ModuleRegistrationOptions registration, TenancyOptions tenancy)
    {
        if (!PlatformModes.IsStandalone(tenancy))
        {
            return new IndicatorPublishingSettings($"/{registration.RoutePath.Trim('/')}/", PlatformPermissions.Access);
        }

        // Standalone: the Shell does not know the permissions of Brocal; links go to the public site (https only).
        var url = indicators.PublicUrl?.Trim();
        var link = !string.IsNullOrEmpty(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? $"{url.TrimEnd('/')}/" : null;
        return new IndicatorPublishingSettings(link, null);
    }
}

/// <summary>Indicator API of the Shell with the service account (<c>chronolith-module-registrar</c>), as the module contract 7.4 describes it.</summary>
internal sealed class ShellIndicatorClient(
    HttpClient http,
    ShellServiceAccountTokenProvider tokens,
    IOptions<IndicatorOptions> indicators,
    IOptions<ShellOptions> shell,
    IOptions<KeycloakOptions> keycloak,
    IOptions<TenancyOptions> tenancy,
    ILogger<ShellIndicatorClient> logger) : IIndicatorClient
{
    public bool IsConfigured => IndicatorAvailability.IsEnabled(indicators.Value, shell.Value, keycloak.Value, tenancy.Value);

    public Task<IndicatorSendResult> RegisterAsync(string definitionsJson, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"api/indicators/sources/{BrocalIndicators.Source}", definitionsJson, cancellationToken);

    public Task<IndicatorSendResult> PublishAsync(string valuesJson, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, "api/indicators/values", valuesJson, cancellationToken);

    public Task<IndicatorSendResult> RetireAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, path, null, cancellationToken);

    private async Task<IndicatorSendResult> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new IndicatorSendResult(IndicatorSendOutcome.Disabled);
        }

        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new IndicatorSendResult(IndicatorSendOutcome.Sent);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = $"{(int)response.StatusCode} {method} {path}: {(content.Length > 500 ? content[..500] : content)}";
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // 403 (ModuleService policy): the account lacks the role; a new token carries it as soon as it is granted.
                tokens.Invalidate();
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                detail += " (la cuenta de servicio necesita el rol de realm chronolith-module-registrar del realm chronolith)";
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new IndicatorSendResult(IndicatorSendOutcome.Rejected, detail),
                HttpStatusCode.NotFound => new IndicatorSendResult(IndicatorSendOutcome.NotFound, detail),
                _ => new IndicatorSendResult(IndicatorSendOutcome.Unavailable, detail)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "El Shell no respondió a {Method} {Path}", method, path);
            return new IndicatorSendResult(IndicatorSendOutcome.Unavailable, ex.Message);
        }
    }
}

/// <summary>The indicator outbox over the Brocal database (global table, same unit of work as the data it reads).</summary>
internal sealed class IndicatorOutboxStore(BrocalDbContext db) : IIndicatorOutbox
{
    public void Add(IndicatorMessage message) => db.IndicatorMessages.Add(message);

    public async Task<IReadOnlyList<IndicatorMessage>> PendingRecalculationsAsync(int take, CancellationToken cancellationToken) =>
        await db.IndicatorMessages
            .Where(m => m.Kind == IndicatorMessageKind.Recalculate && m.Status == IndicatorMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<IndicatorMessage>> PendingDeliveriesAsync(int take, CancellationToken cancellationToken) =>
        await db.IndicatorMessages
            .Where(m => m.Kind != IndicatorMessageKind.Recalculate && m.Status == IndicatorMessageStatus.Pending)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>Finished messages created before the cut-off; pending ones (a retirement, a past day) are never dropped.</summary>
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken cancellationToken) =>
        db.IndicatorMessages.Where(m => m.CreatedAt < before && m.Status != IndicatorMessageStatus.Pending).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IndicatorSubject>> SubjectsAsync(IReadOnlyCollection<Guid>? subjectIds, CancellationToken cancellationToken)
    {
        if (subjectIds is { Count: 0 })
        {
            return new Dictionary<Guid, IndicatorSubject>();
        }

        var query = db.IndicatorSubjects.AsQueryable();
        if (subjectIds is not null)
        {
            var ids = subjectIds.ToList();
            query = query.Where(s => ids.Contains(s.Id));
        }

        return await query.ToDictionaryAsync(s => s.Id, cancellationToken);
    }

    public void AddSubject(IndicatorSubject subject) => db.IndicatorSubjects.Add(subject);
}
