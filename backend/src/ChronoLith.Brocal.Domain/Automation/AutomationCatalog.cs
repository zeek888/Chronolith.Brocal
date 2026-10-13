using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.Automation;

/// <summary>Documents whose life cycle can carry approval workflows and business rules.</summary>
public enum AutomationDocument
{
    ServiceTicket = 0,
    WorkOrder = 1,
    Quote = 2,
    Invoice = 3,
    ExpenseReport = 4,
    MaintenanceOrder = 5,
    FuelLoad = 6,
    InventoryMovement = 7
}

/// <summary>Moments of the life cycle where business rules are evaluated and workflows hold the document.</summary>
public enum AutomationTrigger
{
    /// <summary>Create or edit.</summary>
    Save = 0,

    /// <summary>Ticket signed or closed; workshop order closed.</summary>
    Close = 1,

    /// <summary>Ticket validated by the office; expense report approved.</summary>
    Approve = 2,

    /// <summary>Work order dispatched to the field.</summary>
    Dispatch = 3,

    /// <summary>Quote sent to the customer (or accepted without having been sent).</summary>
    Send = 4,

    /// <summary>Invoice stamped by the PAC.</summary>
    Stamp = 5,

    /// <summary>Expense report submitted for approval.</summary>
    Submit = 6
}

public enum FactType
{
    Number = 0,
    Money = 1,
    Boolean = 2,
    Enum = 3,
    Reference = 4
}

public enum ConditionOperator
{
    Equal = 0,
    NotEqual = 1,
    GreaterThan = 2,
    GreaterOrEqual = 3,
    LessThan = 4,
    LessOrEqual = 5,
    In = 6,
    NotIn = 7,
    IsTrue = 8,
    IsFalse = 9
}

public enum ConditionMatch
{
    All = 0,
    Any = 1
}

/// <summary>What the approval of the last step of a workflow does.</summary>
public enum WorkflowCompletion
{
    /// <summary>The approval is the transition itself: the expense report is approved, the ticket validated.</summary>
    Execute = 0,

    /// <summary>The approval unlocks the action, which the user then performs (dispatch, send, stamp, close).</summary>
    Unlock = 1
}

/// <summary>
/// A datum of a document that conditions can test (total, discount, personnel with expired documents…).
/// <see cref="Volatile"/> facts depend on the moment (days since the service, overdue balance of the customer):
/// they do not invalidate an approval already given.
/// </summary>
public sealed record FactDefinition(
    string Key,
    FactType Type,
    string Name,
    string NameEn,
    IReadOnlyList<string>? Values = null,
    string? EnumName = null,
    string? Catalog = null,
    bool Volatile = false)
{
    /// <summary>References point to records of one company: only company rules can use them.</summary>
    public bool CompanyOnly => Type == FactType.Reference;
}

/// <summary>How a workflow holds a document: when it starts and what its approval does.</summary>
public sealed record WorkflowGate(AutomationTrigger Start, WorkflowCompletion Completion, string Description, string DescriptionEn);

public sealed record AutomationDocumentDefinition(
    AutomationDocument Document,
    string Module,
    string Name,
    string NameEn,
    string Icon,
    string LinkPrefix,
    bool HasDetailPage,
    IReadOnlyList<AutomationTrigger> Triggers,
    WorkflowGate? Workflow,
    IReadOnlyList<FactDefinition> Facts)
{
    public FactDefinition? Fact(string key) => Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Route inside Brocal (notification links).</summary>
    public string LinkOf(Guid id) => HasDetailPage ? $"{LinkPrefix}/{id}" : LinkPrefix;
}

/// <summary>
/// Documents, moments and data that workflows and business rules can use. The administrator builds conditions with
/// these facts from the portal; the application computes them for each document when the rules are evaluated.
/// </summary>
public static class AutomationCatalog
{
    private static readonly string[] Currencies = ["MXN", "USD"];

    private static FactDefinition Customer => new("customerId", FactType.Reference, "Cliente", "Customer", Catalog: "catalogs/customers");

    private static FactDefinition Site => new("siteId", FactType.Reference, "Locación o pozo", "Site or well", Catalog: "catalogs/sites");

    private static FactDefinition ServiceType => new("serviceTypeId", FactType.Reference, "Tipo de servicio", "Service type", Catalog: "pricing/service-types");

    private static FactDefinition Unit => new("unitId", FactType.Reference, "Unidad", "Unit", Catalog: "fleet/units");

    private static FactDefinition ExpiredPersonnel => new("personnelWithExpiredDocuments", FactType.Number, "Personal con documentos vencidos", "Personnel with expired documents", Volatile: true);

    private static FactDefinition ExpiredUnits => new("unitsWithExpiredDocuments", FactType.Number, "Unidades con permisos vencidos", "Units with expired permits", Volatile: true);

    public static readonly IReadOnlyList<AutomationDocumentDefinition> All =
    [
        new(AutomationDocument.ServiceTicket, BrocalModules.Tickets, "Boleta de servicio", "Service ticket", "receipt_long", "boletas", true,
            [AutomationTrigger.Save, AutomationTrigger.Close, AutomationTrigger.Approve],
            new WorkflowGate(AutomationTrigger.Close, WorkflowCompletion.Execute,
                "Empieza al firmar o cerrar la boleta; al autorizar el último paso la boleta queda validada.",
                "Starts when the ticket is signed or closed; approving the last step validates the ticket."),
            [
                new("total", FactType.Money, "Total", "Total"),
                new("subtotal", FactType.Money, "Subtotal", "Subtotal"),
                new("lineCount", FactType.Number, "Número de conceptos", "Number of lines"),
                new("manualPriceLines", FactType.Number, "Conceptos con precio modificado", "Lines with an overridden price"),
                new("photoCount", FactType.Number, "Fotos de evidencia", "Evidence photos"),
                new("hasSignature", FactType.Boolean, "Tiene la firma del cliente", "Has the customer's signature"),
                new("durationHours", FactType.Number, "Duración del servicio (horas)", "Service duration (hours)"),
                new("daysSinceService", FactType.Number, "Días desde el servicio", "Days since the service", Volatile: true),
                new("personnelCount", FactType.Number, "Personal en la boleta", "Personnel on the ticket"),
                new("unitCount", FactType.Number, "Unidades en la boleta", "Units on the ticket"),
                ExpiredPersonnel,
                ExpiredUnits,
                new("hasWorkOrder", FactType.Boolean, "Viene de una orden de trabajo", "Comes from a work order"),
                new("contractExpired", FactType.Boolean, "Contrato vencido o inactivo", "Expired or inactive contract", Volatile: true),
                Customer,
                ServiceType,
                Site
            ]),

        new(AutomationDocument.WorkOrder, BrocalModules.Dispatch, "Orden de trabajo", "Work order", "event_note", "ordenes", true,
            [AutomationTrigger.Save, AutomationTrigger.Dispatch],
            new WorkflowGate(AutomationTrigger.Dispatch, WorkflowCompletion.Unlock,
                "Se solicita al despachar; al autorizarse se puede despachar la orden.",
                "Requested when dispatching; once approved the order can be dispatched."),
            [
                new("priority", FactType.Enum, "Prioridad", "Priority", ["low", "normal", "high", "urgent"], "workOrderPriority"),
                new("unitCount", FactType.Number, "Unidades asignadas", "Assigned units"),
                new("personnelCount", FactType.Number, "Personal asignado", "Assigned personnel"),
                new("hasCrew", FactType.Boolean, "Tiene cuadrilla", "Has a crew"),
                new("durationHours", FactType.Number, "Duración programada (horas)", "Scheduled duration (hours)"),
                new("hoursUntilStart", FactType.Number, "Horas para el inicio", "Hours until the start", Volatile: true),
                ExpiredPersonnel,
                ExpiredUnits,
                new("unitsNotAvailable", FactType.Number, "Unidades en taller o fuera de servicio", "Units in the workshop or out of service", Volatile: true),
                new("fromQuote", FactType.Boolean, "Proviene de una cotización", "Comes from a quote"),
                Customer,
                ServiceType,
                Site
            ]),

        new(AutomationDocument.Quote, BrocalModules.Quotes, "Cotización", "Quote", "request_page", "cotizaciones", true,
            [AutomationTrigger.Save, AutomationTrigger.Send],
            new WorkflowGate(AutomationTrigger.Send, WorkflowCompletion.Unlock,
                "Se solicita al enviar la cotización; al autorizarse se puede enviar o marcar como aceptada.",
                "Requested when sending the quote; once approved it can be sent or marked as accepted."),
            [
                new("total", FactType.Money, "Total", "Total"),
                new("subtotal", FactType.Money, "Subtotal", "Subtotal"),
                new("discount", FactType.Money, "Descuento", "Discount"),
                new("discountPercent", FactType.Number, "Descuento global (%)", "Overall discount (%)"),
                new("maxLineDiscountPercent", FactType.Number, "Mayor descuento de un concepto (%)", "Largest line discount (%)"),
                new("lineCount", FactType.Number, "Número de conceptos", "Number of lines"),
                new("validityDays", FactType.Number, "Días de vigencia", "Validity (days)"),
                new("currency", FactType.Enum, "Moneda", "Currency", Currencies),
                new("hasContract", FactType.Boolean, "Ligada a un contrato", "Linked to a contract"),
                Customer
            ]),

        new(AutomationDocument.Invoice, BrocalModules.Invoicing, "Factura", "Invoice", "request_quote", "facturacion/facturas", true,
            [AutomationTrigger.Stamp],
            new WorkflowGate(AutomationTrigger.Stamp, WorkflowCompletion.Unlock,
                "Se solicita al timbrar; al autorizarse se puede timbrar la factura. No aplica a la Carta Porte de traslado.",
                "Requested when stamping; once approved the invoice can be stamped. Not applied to transfer Carta Porte."),
            [
                new("total", FactType.Money, "Total", "Total"),
                new("subtotal", FactType.Money, "Subtotal", "Subtotal"),
                new("paymentMethod", FactType.Enum, "Método de pago", "Payment method", ["PUE", "PPD"]),
                new("currency", FactType.Enum, "Moneda", "Currency", Currencies),
                new("ticketCount", FactType.Number, "Boletas incluidas", "Tickets included"),
                new("lineCount", FactType.Number, "Número de conceptos", "Number of lines"),
                new("creditDays", FactType.Number, "Días de crédito", "Credit days"),
                new("hasCopade", FactType.Boolean, "Tiene COPADE", "Has a COPADE"),
                new("isManual", FactType.Boolean, "Factura manual (sin boletas)", "Manual invoice (no tickets)"),
                new("customerOverdueBalance", FactType.Money, "Saldo vencido del cliente", "Customer's overdue balance", Volatile: true),
                Customer
            ]),

        new(AutomationDocument.ExpenseReport, BrocalModules.Expenses, "Comprobación de viáticos", "Expense report", "receipt", "viaticos", true,
            [AutomationTrigger.Save, AutomationTrigger.Submit, AutomationTrigger.Approve],
            new WorkflowGate(AutomationTrigger.Submit, WorkflowCompletion.Execute,
                "Empieza al enviar la comprobación; al decidir el último paso queda autorizada o rechazada.",
                "Starts when the report is submitted; the last decision approves or rejects it."),
            [
                new("total", FactType.Money, "Total comprobado", "Total proven"),
                new("advanceAmount", FactType.Money, "Anticipo", "Advance"),
                new("balance", FactType.Money, "Saldo (anticipo − comprobado)", "Balance (advance − proven)"),
                new("itemCount", FactType.Number, "Número de gastos", "Number of expenses"),
                new("itemsWithoutInvoice", FactType.Number, "Gastos sin CFDI", "Expenses without CFDI"),
                new("amountWithoutInvoice", FactType.Money, "Importe sin CFDI", "Amount without CFDI"),
                new("itemsWithoutReceipt", FactType.Number, "Gastos sin foto del comprobante", "Expenses without a receipt photo"),
                new("maxItemAmount", FactType.Money, "Gasto más alto", "Largest expense"),
                new("periodDays", FactType.Number, "Días del periodo", "Days of the period"),
                new("hasWorkOrder", FactType.Boolean, "Ligada a una orden de trabajo", "Linked to a work order"),
                new("employeeId", FactType.Reference, "Empleado", "Employee", Catalog: "personnel/employees")
            ]),

        new(AutomationDocument.MaintenanceOrder, BrocalModules.Maintenance, "Orden de taller", "Workshop order", "build", "mantenimiento/ordenes", true,
            [AutomationTrigger.Save, AutomationTrigger.Close],
            new WorkflowGate(AutomationTrigger.Close, WorkflowCompletion.Unlock,
                "Se solicita al cerrar la orden; al autorizarse se puede cerrar y liberar la unidad.",
                "Requested when closing the order; once approved it can be closed and the unit released."),
            [
                new("totalCost", FactType.Money, "Costo total", "Total cost"),
                new("partsCost", FactType.Money, "Costo de refacciones", "Parts cost"),
                new("laborCost", FactType.Money, "Costo de mano de obra", "Labor cost"),
                new("externalCost", FactType.Money, "Costo externo", "External cost"),
                new("type", FactType.Enum, "Tipo", "Type", ["preventive", "corrective"], "maintenanceType"),
                new("pendingTasks", FactType.Number, "Tareas sin terminar", "Unfinished tasks"),
                new("downtimeHours", FactType.Number, "Horas en taller", "Hours in the workshop", Volatile: true),
                Unit
            ]),

        new(AutomationDocument.FuelLoad, BrocalModules.Fuel, "Carga de combustible", "Fuel load", "local_gas_station", "combustible/cargas", false,
            [AutomationTrigger.Save],
            null,
            [
                new("liters", FactType.Number, "Litros", "Liters"),
                new("amount", FactType.Money, "Importe", "Amount"),
                new("pricePerLiter", FactType.Money, "Precio por litro", "Price per liter"),
                new("isFullTank", FactType.Boolean, "Tanque lleno", "Full tank"),
                new("hasReceipt", FactType.Boolean, "Tiene foto del ticket", "Has a receipt photo"),
                new("deviationPercent", FactType.Number, "Desviación del rendimiento (%)", "Efficiency deviation (%)"),
                new("isAnomaly", FactType.Boolean, "Rendimiento fuera de rango", "Out-of-range efficiency"),
                new("paymentMethod", FactType.Enum, "Forma de pago", "Payment method", ["cash", "card", "voucher", "credit"], "fuelPaymentMethod"),
                Unit
            ]),

        new(AutomationDocument.InventoryMovement, BrocalModules.Inventory, "Movimiento de almacén", "Stock movement", "swap_horiz", "inventario/movimientos", false,
            [AutomationTrigger.Save],
            null,
            [
                new("type", FactType.Enum, "Tipo de movimiento", "Movement type", ["receipt", "issue", "transfer", "adjustment", "maintenance", "crewIssue", "return"], "movementType"),
                new("quantity", FactType.Number, "Cantidad", "Quantity"),
                new("totalCost", FactType.Money, "Costo total", "Total cost"),
                new("stockAfter", FactType.Number, "Existencia resultante en el almacén", "Resulting stock in the warehouse"),
                new("belowMinimum", FactType.Boolean, "Queda debajo del mínimo", "Ends below the minimum"),
                new("itemId", FactType.Reference, "Artículo", "Item", Catalog: "inventory/items"),
                new("warehouseId", FactType.Reference, "Almacén", "Warehouse", Catalog: "inventory/warehouses")
            ])
    ];

    /// <summary>Spanish and English names of the moments (labels of the portal and of the messages).</summary>
    public static readonly IReadOnlyDictionary<AutomationTrigger, (string Name, string NameEn)> TriggerNames = new Dictionary<AutomationTrigger, (string, string)>
    {
        [AutomationTrigger.Save] = ("Al guardar", "On save"),
        [AutomationTrigger.Close] = ("Al cerrar o firmar", "On close or sign"),
        [AutomationTrigger.Approve] = ("Al validar o autorizar", "On validation or approval"),
        [AutomationTrigger.Dispatch] = ("Al despachar", "On dispatch"),
        [AutomationTrigger.Send] = ("Al enviar al cliente", "On sending to the customer"),
        [AutomationTrigger.Stamp] = ("Al timbrar", "On stamping"),
        [AutomationTrigger.Submit] = ("Al enviar a autorización", "On submission")
    };

    private static readonly Dictionary<AutomationDocument, AutomationDocumentDefinition> ByDocument = All.ToDictionary(d => d.Document);

    public static AutomationDocumentDefinition Of(AutomationDocument document) => ByDocument[document];
}

/// <summary>"total > 50000", "priority in high,urgent", "hasSignature is false"… evaluated against the facts of a document.</summary>
public sealed record RuleCondition(string Field, ConditionOperator Operator, string? Value);

public static class RuleConditions
{
    public const int MaxConditions = 10;

    /// <summary>Checks fields, operators and values against the facts of the document and returns them in canonical form.</summary>
    public static Result<IReadOnlyList<RuleCondition>> Normalize(AutomationDocumentDefinition document, IEnumerable<RuleCondition>? conditions, bool isGlobal)
    {
        var normalized = new List<RuleCondition>();
        foreach (var condition in conditions ?? [])
        {
            var fact = document.Fact(condition.Field ?? string.Empty);
            if (fact is null)
            {
                return AutomationErrors.UnknownField(condition.Field ?? string.Empty);
            }

            if (isGlobal && fact.CompanyOnly)
            {
                return AutomationErrors.CompanyOnlyField(fact.Name);
            }

            if (!IsAllowed(fact.Type, condition.Operator))
            {
                return AutomationErrors.InvalidOperator(fact.Name);
            }

            var value = NormalizeValue(fact, condition.Operator, condition.Value);
            if (value.IsFailure)
            {
                return value.Error;
            }

            normalized.Add(new RuleCondition(fact.Key, condition.Operator, value.Value));
        }

        return normalized.Count > MaxConditions ? AutomationErrors.TooManyConditions : normalized;
    }

    /// <summary>No conditions: always applies.</summary>
    public static bool Matches(IReadOnlyList<RuleCondition> conditions, ConditionMatch match, IReadOnlyDictionary<string, object?> facts)
    {
        if (conditions.Count == 0)
        {
            return true;
        }

        return match == ConditionMatch.All ? conditions.All(c => Evaluate(c, facts)) : conditions.Any(c => Evaluate(c, facts));
    }

    /// <summary>A missing fact (no contract, no efficiency computed) never satisfies a comparison; it satisfies "not equal" and "not in".</summary>
    public static bool Evaluate(RuleCondition condition, IReadOnlyDictionary<string, object?> facts)
    {
        facts.TryGetValue(condition.Field, out var fact);
        switch (condition.Operator)
        {
            case ConditionOperator.IsTrue:
                return fact is true;
            case ConditionOperator.IsFalse:
                return fact is not true;
            case ConditionOperator.GreaterThan or ConditionOperator.GreaterOrEqual or ConditionOperator.LessThan or ConditionOperator.LessOrEqual:
                if (AsNumber(fact) is not { } number || !TryNumber(condition.Value, out var limit))
                {
                    return false;
                }

                return condition.Operator switch
                {
                    ConditionOperator.GreaterThan => number > limit,
                    ConditionOperator.GreaterOrEqual => number >= limit,
                    ConditionOperator.LessThan => number < limit,
                    _ => number <= limit
                };
            case ConditionOperator.Equal:
                return IsEqual(fact, condition.Value);
            case ConditionOperator.NotEqual:
                return !IsEqual(fact, condition.Value);
            case ConditionOperator.In:
                return Split(condition.Value).Any(v => IsEqual(fact, v));
            case ConditionOperator.NotIn:
                return !Split(condition.Value).Any(v => IsEqual(fact, v));
            default:
                return false;
        }
    }

    /// <summary>
    /// Fingerprint of the data a decision was taken on (volatile facts excluded): an approval is valid while the
    /// document keeps the same data.
    /// </summary>
    public static string Hash(AutomationDocumentDefinition document, IReadOnlyDictionary<string, object?> facts)
    {
        var text = new StringBuilder();
        foreach (var fact in document.Facts.Where(f => !f.Volatile).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            facts.TryGetValue(fact.Key, out var value);
            text.Append(fact.Key).Append('=').Append(Format(value)).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..32].ToLowerInvariant();
    }

    /// <summary>Invariant text of a fact (snapshots, hashes and message placeholders).</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static bool IsAllowed(FactType type, ConditionOperator op) => type switch
    {
        FactType.Number or FactType.Money => op is ConditionOperator.Equal or ConditionOperator.NotEqual or ConditionOperator.GreaterThan or ConditionOperator.GreaterOrEqual
            or ConditionOperator.LessThan or ConditionOperator.LessOrEqual,
        FactType.Boolean => op is ConditionOperator.IsTrue or ConditionOperator.IsFalse,
        _ => op is ConditionOperator.Equal or ConditionOperator.NotEqual or ConditionOperator.In or ConditionOperator.NotIn
    };

    private static Result<string?> NormalizeValue(FactDefinition fact, ConditionOperator op, string? value)
    {
        switch (fact.Type)
        {
            case FactType.Boolean:
                return Result.Success<string?>(null);

            case FactType.Number or FactType.Money:
                return TryNumber(value, out var number)
                    ? Result.Success<string?>(number.ToString(CultureInfo.InvariantCulture))
                    : AutomationErrors.InvalidValue(fact.Name);

            case FactType.Enum:
            {
                var values = Split(value).Select(v => fact.Values?.FirstOrDefault(allowed => string.Equals(allowed, v, StringComparison.OrdinalIgnoreCase))).ToList();
                if (values.Count == 0 || values.Any(v => v is null) || op is ConditionOperator.Equal or ConditionOperator.NotEqual && values.Count != 1)
                {
                    return AutomationErrors.InvalidValue(fact.Name);
                }

                return Result.Success<string?>(string.Join(',', values.Distinct()));
            }

            default:
            {
                var ids = Split(value).Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty).ToList();
                if (ids.Count == 0 || ids.Any(id => id == Guid.Empty) || op is ConditionOperator.Equal or ConditionOperator.NotEqual && ids.Count != 1)
                {
                    return AutomationErrors.InvalidValue(fact.Name);
                }

                return Result.Success<string?>(string.Join(',', ids.Distinct()));
            }
        }
    }

    private static bool IsEqual(object? fact, string? value)
    {
        if (fact is null || value is null)
        {
            return false;
        }

        if (AsNumber(fact) is { } number)
        {
            return TryNumber(value, out var other) && number == other;
        }

        return string.Equals(Format(fact), value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? AsNumber(object? value) => value switch
    {
        decimal d => d,
        int i => i,
        long l => l,
        double d => (decimal)d,
        _ => null
    };

    private static bool TryNumber(string? value, out decimal number) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static List<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

public static class AutomationErrors
{
    public static readonly Error NotFound = Error.NotFound("Automation.NotFound", "La definición no existe.");

    public static readonly Error InvalidName = Error.Validation("Automation.InvalidName", "El nombre es obligatorio (máximo 120 caracteres) y la descripción admite 500.");

    public static readonly Error InvalidPriority = Error.Validation("Automation.InvalidPriority", "El orden va de 0 a 999.");

    public static readonly Error TooManyConditions = Error.Validation("Automation.TooManyConditions", $"Se admiten hasta {RuleConditions.MaxConditions} condiciones.");

    public static readonly Error NoWorkflow = Error.Validation("Automation.NoWorkflow", "Ese documento no admite flujos de autorización.");

    public static readonly Error InvalidSteps = Error.Validation("Automation.InvalidSteps", "Un flujo tiene de 1 a 6 pasos; cada paso necesita nombre (máximo 80 caracteres), al menos un aprobador, de 1 a 5 aprobaciones y un plazo de 1 a 720 horas.");

    public static readonly Error InvalidTriggers = Error.Validation("Automation.InvalidTriggers", "Elige al menos un momento válido para el documento.");

    public static readonly Error ConditionsRequired = Error.Validation("Automation.ConditionsRequired", "Una regla necesita al menos una condición.");

    public static readonly Error InvalidMessage = Error.Validation("Automation.InvalidMessage", "El mensaje es obligatorio (máximo 500 caracteres).");

    public static readonly Error RecipientsRequired = Error.Validation("Automation.RecipientsRequired", "Indica a quién avisar (roles, permisos, administradores, correos o a quien hizo la operación).");

    public static readonly Error UnknownRoleTemplate = Error.Validation("Automation.UnknownRoleTemplate", "Uno de los roles base no existe.");

    public static readonly Error UnknownPermission = Error.Validation("Automation.UnknownPermission", "Uno de los permisos no existe.");

    public static readonly Error CompanyRolesInGlobal = Error.Validation(
        "Automation.CompanyRolesInGlobal", "Las definiciones globales no pueden usar roles de una empresa: usa roles base, permisos o administradores.");

    public static readonly Error GlobalOnly = Error.Forbidden("Automation.GlobalOnly", "Solo el administrador de la plataforma crea o cambia definiciones globales.");

    public static readonly Error NotOptional = Error.Conflict("Automation.NotOptional", "La definición es obligatoria para todas las empresas.");

    public static readonly Error NotGlobal = Error.Validation("Automation.NotGlobal", "Solo las definiciones globales se pueden desactivar por empresa.");

    public static Error UnknownField(string field) => Error.Validation("Automation.UnknownField", $"El dato '{field}' no existe para este documento.");

    public static Error CompanyOnlyField(string field) => Error.Validation(
        "Automation.CompanyOnlyField", $"'{field}' depende de los catálogos de cada empresa: úsalo en una definición de la empresa, no en una global.");

    public static Error InvalidOperator(string field) => Error.Validation("Automation.InvalidOperator", $"La comparación no aplica al dato '{field}'.");

    public static Error InvalidValue(string field) => Error.Validation("Automation.InvalidValue", $"El valor de '{field}' no es válido.");
}
