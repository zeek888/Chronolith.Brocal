using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Application.Features.Indicators;

/// <summary>Definition of one indicator as <c>PUT /api/indicators/sources/brocal</c> declares it (texts in es, en and pt-BR).</summary>
public sealed record IndicatorDefinition(
    string Key,
    string Type,
    string Aggregation,
    string Accumulation,
    string Direction,
    int Decimals,
    string Name,
    string Description,
    string NameEn,
    string DescriptionEn,
    string NamePt,
    string DescriptionPt);

/// <summary>
/// Indicators Brocal publishes to the ChronoLith Shell (source <c>brocal</c>): what the management of the operator
/// wants to see per well or facility of the shared catalog, adding up every company of the installation. The subject
/// is the well or facility a site is linked to (<c>Site.ShellCatalogId</c>); sites without a link publish nothing.
/// </summary>
public static class BrocalIndicators
{
    public const string Source = PlatformPermissions.ModuleCode;

    public const string Well = "well";
    public const string Facility = "facility";

    public const string DocumentsValid = "documents.valid";
    public const string DocumentsExpiring = "documents.expiring";
    public const string DocumentsExpired = "documents.expired";
    public const string DocumentsMissing = "documents.missing";
    public const string DocumentsCompliance = "documents.compliance";
    public const string DocumentsNextExpiry = "documents.next-expiry";
    public const string TicketsServices = "tickets.services";
    public const string TicketsUnsigned = "tickets.unsigned";
    public const string WorkOrdersOpen = "work-orders.open";
    public const string HseIncidents = "hse.incidents";
    public const string HseOpenIncidents = "hse.open-incidents";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<IndicatorDefinition> All { get; } =
    [
        new(DocumentsValid, "number", "sum", "snapshot", "higherIsBetter", 0,
            "Documentos vigentes",
            "Permisos de unidades y documentos del personal asignados a órdenes de trabajo abiertas en el pozo o instalación, vigentes fuera de la ventana de aviso o sin vencimiento.",
            "Valid documents",
            "Unit permits and personnel documents assigned to open work orders at the well or facility, valid beyond the warning window or without expiry.",
            "Documentos vigentes",
            "Licenças das unidades e documentos do pessoal designados a ordens de serviço abertas no poço ou instalação, vigentes além da janela de aviso ou sem vencimento."),
        new(DocumentsExpiring, "number", "sum", "snapshot", "lowerIsBetter", 0,
            "Documentos por vencer",
            "Documentos obligatorios o requeridos por el cliente del personal y las unidades asignados a órdenes abiertas que vencen dentro de la ventana de aviso de su empresa.",
            "Documents expiring soon",
            "Mandatory or customer-required documents of the personnel and units assigned to open work orders that expire within their company's warning window.",
            "Documentos a vencer",
            "Documentos obrigatórios ou exigidos pelo cliente do pessoal e das unidades designados a ordens abertas que vencem dentro da janela de aviso da sua empresa."),
        new(DocumentsExpired, "number", "sum", "snapshot", "lowerIsBetter", 0,
            "Documentos vencidos",
            "Documentos obligatorios o requeridos por el cliente del personal y las unidades asignados a órdenes abiertas que ya vencieron.",
            "Expired documents",
            "Mandatory or customer-required documents of the personnel and units assigned to open work orders that have already expired.",
            "Documentos vencidos",
            "Documentos obrigatórios ou exigidos pelo cliente do pessoal e das unidades designados a ordens abertas que já venceram."),
        new(DocumentsMissing, "number", "sum", "snapshot", "lowerIsBetter", 0,
            "Documentos faltantes",
            "Documentos obligatorios o requeridos por el cliente que no se han capturado para el personal y las unidades asignados a órdenes abiertas.",
            "Missing documents",
            "Mandatory or customer-required documents not captured for the personnel and units assigned to open work orders.",
            "Documentos faltantes",
            "Documentos obrigatórios ou exigidos pelo cliente não cadastrados para o pessoal e as unidades designados a ordens abertas."),
        new(DocumentsCompliance, "percent", "avg", "snapshot", "higherIsBetter", 1,
            "Cumplimiento documental",
            "Porcentaje de los documentos requeridos del personal y las unidades asignados a órdenes abiertas que están vigentes (incluye los por vencer).",
            "Document compliance",
            "Percentage of the required documents of the personnel and units assigned to open work orders that are in force (expiring ones included).",
            "Conformidade documental",
            "Percentual dos documentos exigidos do pessoal e das unidades designados a ordens abertas que estão vigentes (inclui os a vencer)."),
        new(DocumentsNextExpiry, "date", "none", "snapshot", "neutral", 0,
            "Próximo vencimiento",
            "Fecha en que vence el siguiente documento vigente del personal y las unidades asignados a órdenes abiertas.",
            "Next expiry",
            "Date on which the next valid document of the personnel and units assigned to open work orders expires.",
            "Próximo vencimento",
            "Data em que vence o próximo documento vigente do pessoal e das unidades designados a ordens abertas."),
        new(TicketsServices, "number", "sum", "daily", "neutral", 0,
            "Boletas de servicio",
            "Boletas de servicio del día (sin las canceladas) de todas las empresas en el pozo o instalación.",
            "Service tickets",
            "Service tickets of the day (cancelled ones excluded) of every company at the well or facility.",
            "Boletins de serviço",
            "Boletins de serviço do dia (sem os cancelados) de todas as empresas no poço ou instalação."),
        new(TicketsUnsigned, "number", "sum", "snapshot", "lowerIsBetter", 0,
            "Boletas sin firmar",
            "Boletas en captura que aún no se cierran con la firma del cliente.",
            "Unsigned tickets",
            "Tickets still being captured, not yet closed with the customer's signature.",
            "Boletins sem assinatura",
            "Boletins em preenchimento ainda não encerrados com a assinatura do cliente."),
        new(WorkOrdersOpen, "number", "sum", "snapshot", "neutral", 0,
            "Órdenes de trabajo abiertas",
            "Órdenes programadas, despachadas o en proceso en el pozo o instalación.",
            "Open work orders",
            "Work orders scheduled, dispatched or in progress at the well or facility.",
            "Ordens de serviço abertas",
            "Ordens programadas, despachadas ou em andamento no poço ou instalação."),
        new(HseIncidents, "number", "sum", "daily", "lowerIsBetter", 0,
            "Incidentes HSE",
            "Incidentes, accidentes, casi accidentes y condiciones inseguras reportados en el día en el pozo o instalación.",
            "HSE incidents",
            "Incidents, accidents, near misses and unsafe conditions reported on the day at the well or facility.",
            "Incidentes de SSMA",
            "Incidentes, acidentes, quase acidentes e condições inseguras reportados no dia no poço ou instalação."),
        new(HseOpenIncidents, "number", "sum", "snapshot", "lowerIsBetter", 0,
            "Incidentes HSE abiertos",
            "Incidentes reportados o en investigación que aún no se cierran.",
            "Open HSE incidents",
            "Incidents reported or under investigation that are not closed yet.",
            "Incidentes de SSMA abertos",
            "Incidentes reportados ou em investigação ainda não encerrados.")
    ];

    public static IReadOnlyList<string> Keys { get; } = All.Select(d => d.Key).ToList();

    /// <summary>Subject type of the Shell for a catalog of the replica (<c>wells</c>, <c>facilities</c>); null for the others.</summary>
    public static string? SubjectTypeOf(string? catalog) => catalog switch
    {
        SharedCatalogNames.Wells => Well,
        SharedCatalogNames.Facilities => Facility,
        _ => null
    };

    /// <summary>Body of <c>PUT /api/indicators/sources/brocal</c>.</summary>
    public static string DefinitionsJson(IndicatorPublishingSettings settings) => JsonSerializer.Serialize(new
    {
        name = "Brocal",
        translations = new Dictionary<string, object>
        {
            ["en"] = new { name = "Brocal" },
            ["pt-BR"] = new { name = "Brocal" }
        },
        indicators = All.Select(d => new
        {
            key = d.Key,
            name = d.Name,
            description = d.Description,
            type = d.Type,
            quantity = (string?)null,
            unit = (string?)null,
            decimals = d.Decimals,
            subjectTypes = new[] { Well, Facility },
            aggregation = d.Aggregation,
            accumulation = d.Accumulation,
            direction = d.Direction,
            permissionCode = settings.PermissionCode,
            translations = new Dictionary<string, object>
            {
                ["en"] = new { name = d.NameEn, description = d.DescriptionEn },
                ["pt-BR"] = new { name = d.NamePt, description = d.DescriptionPt }
            }
        })
    }, Json);

    /// <summary>Body of <c>PUT /api/indicators/values</c>.</summary>
    public static string ValuesJson(IEnumerable<IndicatorValue> values) => JsonSerializer.Serialize(new { source = Source, values }, Json);

    /// <summary>
    /// A body of <c>PUT /api/indicators/values</c> split into one body per subject (same order of values), with how
    /// many values each carries. Empty when the payload cannot be read.
    /// </summary>
    public static IReadOnlyList<(string Json, int Items)> SplitBySubject(string valuesJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(valuesJson);
        }
        catch (JsonException)
        {
            return [];
        }

        if (root is not JsonObject body || body["values"] is not JsonArray values)
        {
            return [];
        }

        var source = body["source"] is JsonValue name && name.TryGetValue<string>(out var code) ? code : Source;
        return values
            .Where(v => v is not null)
            .GroupBy(v => v is JsonObject value ? value["subjectId"]?.ToString() ?? string.Empty : string.Empty)
            .Select(g =>
            {
                var items = new JsonArray(g.Select(v => v!.DeepClone()).ToArray());
                return (new JsonObject { ["source"] = source, ["values"] = items }.ToJsonString(), items.Count);
            })
            .ToList();
    }

    /// <summary>Path of <c>DELETE /api/indicators/values/brocal/{key}/{type}/{id}</c> (current value and history of the subject).</summary>
    public static string RetirePath(string key, string subjectType, Guid subjectId) => $"api/indicators/values/{Source}/{key}/{subjectType}/{subjectId}";
}

/// <summary>
/// What changed since the last recalculation: sites whose records changed, subjects of the Shell a site was unlinked
/// from (with their type when known) and business days of the daily indicators. <see cref="All"/> recalculates every
/// active subject (documents changed, daily republication).
/// </summary>
public sealed class IndicatorChangeSet
{
    public bool All { get; set; }

    public HashSet<Guid> Sites { get; init; } = [];

    /// <summary>Well or facility of the Shell → <c>well</c>/<c>facility</c> (null when unknown).</summary>
    public Dictionary<Guid, string?> Subjects { get; init; } = [];

    public HashSet<DateOnly> Days { get; init; } = [];

    [JsonIgnore]
    public bool IsEmpty => !All && Sites.Count == 0 && Subjects.Count == 0;

    public void AddSite(Guid? siteId)
    {
        if (siteId is { } id && id != Guid.Empty)
        {
            Sites.Add(id);
        }
    }

    public void AddSubject(Guid? subjectId, string? subjectType)
    {
        if (subjectId is not { } id || id == Guid.Empty)
        {
            return;
        }

        if (!Subjects.TryGetValue(id, out var known) || known is null)
        {
            Subjects[id] = subjectType;
        }
    }

    public void AddDay(DateOnly? day)
    {
        if (day is { } value)
        {
            Days.Add(value);
        }
    }

    public void Merge(IndicatorChangeSet other)
    {
        All |= other.All;
        Sites.UnionWith(other.Sites);
        Days.UnionWith(other.Days);
        foreach (var (id, type) in other.Subjects)
        {
            AddSubject(id, type);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, BrocalIndicators.Json);

    public static IndicatorChangeSet FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IndicatorChangeSet>(json, BrocalIndicators.Json) ?? new IndicatorChangeSet { All = true };
        }
        catch (JsonException)
        {
            // An unreadable marker still means "something changed": recalculate everything.
            return new IndicatorChangeSet { All = true };
        }
    }
}

/// <summary>Business days of the indicators: the period of a value is the day in Mexico City unless a company has another zone.</summary>
public static class BusinessCalendar
{
    public const string DefaultZone = "America/Mexico_City";

    public static TimeZoneInfo Resolve(string? zone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zone) ? DefaultZone : zone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultZone);
        }
    }

    public static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>Today in the business zone of the installation (period of the snapshot indicators).</summary>
    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow(), Resolve(DefaultZone));
}
