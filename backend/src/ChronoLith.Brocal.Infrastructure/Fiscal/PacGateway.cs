using System.Xml.Linq;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Infrastructure.Fiscal;

/// <summary>Adapter of one PAC (Proveedor Autorizado de Certificación).</summary>
public interface IPacProvider
{
    string Name { get; }

    Task<StampResult> StampAsync(TenantFiscalProfile profile, CfdiDocument document, CancellationToken cancellationToken);

    Task<CancelResult> CancelAsync(TenantFiscalProfile profile, string uuid, string receiverRfc, decimal total, string motive, string? substitutionUuid, CancellationToken cancellationToken);
}

/// <summary>Routes each company to the PAC configured in its fiscal profile.</summary>
internal sealed class PacGateway(IEnumerable<IPacProvider> providers) : IPacGateway
{
    private readonly Dictionary<string, IPacProvider> _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Providers => _providers.Keys.Order().ToList();

    public Task<StampResult> StampAsync(TenantFiscalProfile profile, CfdiDocument document, CancellationToken cancellationToken) =>
        _providers.TryGetValue(profile.PacProvider, out var provider)
            ? provider.StampAsync(profile, document, cancellationToken)
            : Task.FromResult(new StampResult(false, null, null, null, $"El proveedor de timbrado '{profile.PacProvider}' no está disponible.", false));

    public Task<CancelResult> CancelAsync(TenantFiscalProfile profile, string uuid, string receiverRfc, decimal total, string motive, string? substitutionUuid, CancellationToken cancellationToken) =>
        _providers.TryGetValue(profile.PacProvider, out var provider)
            ? provider.CancelAsync(profile, uuid, receiverRfc, total, motive, substitutionUuid, cancellationToken)
            : Task.FromResult(new CancelResult(false, false, null, $"El proveedor de timbrado '{profile.PacProvider}' no está disponible."));
}

/// <summary>
/// PAC "Simulado": builds the real CFDI 4.0 XML and adds a TimbreFiscalDigital with a random UUID and the SAT test
/// RFC. It has no fiscal validity: it is for demos, training and tests. Production companies configure a real PAC.
/// </summary>
internal sealed class SimulatedPacProvider(TimeProvider timeProvider, ILogger<SimulatedPacProvider> logger) : IPacProvider
{
    public string Name => TenantFiscalProfile.SimulatedPac;

    public Task<StampResult> StampAsync(TenantFiscalProfile profile, CfdiDocument document, CancellationToken cancellationToken)
    {
        var xml = CfdiXmlBuilder.Build(document);
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var stampedAt = timeProvider.GetUtcNow();
        CfdiXmlBuilder.AddStamp(xml, uuid, stampedAt, "AAA010101AAA", simulated: true);

        logger.LogInformation("CFDI {Type} {Series}-{Folio} timbrado en modo simulado ({Uuid})", document.Type, document.Series, document.Folio, uuid);
        return Task.FromResult(new StampResult(true, uuid, stampedAt, xml.Declaration + Environment.NewLine + xml.Root, null, true));
    }

    public Task<CancelResult> CancelAsync(TenantFiscalProfile profile, string uuid, string receiverRfc, decimal total, string motive, string? substitutionUuid, CancellationToken cancellationToken) =>
        Task.FromResult(new CancelResult(true, true, "Cancelado (simulado)", null));
}
