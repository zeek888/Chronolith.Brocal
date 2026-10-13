namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>Groups of the module catalog (the order of the menu).</summary>
public enum ModuleCategory
{
    Operations = 0,
    Assets = 1,
    Compliance = 2,
    Fiscal = 3,
    Management = 4,
    Platform = 5
}

/// <summary>A permission of a module. The code is <c>&lt;module&gt;.&lt;action&gt;</c>.</summary>
public sealed record PermissionDefinition(string Code, string Name, string NameEn, string? Description = null, string? DescriptionEn = null)
{
    public string ModuleCode => Code[..Code.IndexOf('.', StringComparison.Ordinal)];
}

/// <summary>
/// A functional module of Brocal. Each tenant enables the modules it contracted; core modules are always on.
/// Enabling a module also enables the modules it depends on.
/// </summary>
public sealed record ModuleDefinition(
    string Code,
    int Number,
    ModuleCategory Category,
    string Name,
    string NameEn,
    string Description,
    string DescriptionEn,
    string Icon,
    bool IsCore,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<PermissionDefinition> Permissions);

/// <summary>Catalog of the 24 Brocal modules (plus the core master data and the workflows and rules) and their permissions.</summary>
public static class BrocalModules
{
    public const string Catalogs = "catalogs";
    public const string Tickets = "tickets";
    public const string Dispatch = "dispatch";
    public const string Pricing = "pricing";
    public const string Billing = "billing";
    public const string Fleet = "fleet";
    public const string Maintenance = "maintenance";
    public const string Permits = "permits";
    public const string Fuel = "fuel";
    public const string Inventory = "inventory";
    public const string Personnel = "personnel";
    public const string Hse = "hse";
    public const string Crews = "crews";
    public const string Expenses = "expenses";
    public const string Invoicing = "invoicing";
    public const string CartaPorte = "cartaporte";
    public const string Collections = "collections";
    public const string Quotes = "quotes";
    public const string Dashboard = "dashboard";
    public const string Portal = "portal";
    public const string Reports = "reports";
    public const string Access = "access";
    public const string Audit = "audit";
    public const string Notifications = "notifications";
    public const string Integrations = "integrations";
    public const string Automation = "automation";

    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        new(Catalogs, 0, ModuleCategory.Operations, "Catálogos base", "Master data",
            "Clientes (operadoras), contratos y locaciones o pozos que comparten todos los módulos.",
            "Customers (operators), contracts and sites or wells shared by every module.",
            "domain", IsCore: true, [],
            [
                P(Permissions.CatalogsRead, "Consultar catálogos base", "View master data"),
                P(Permissions.CatalogsManage, "Administrar catálogos base", "Manage master data", "Clientes, contratos y locaciones.", "Customers, contracts and sites.")
            ]),

        // ---- Núcleo operativo ------------------------------------------------------------------
        new(Tickets, 1, ModuleCategory.Operations, "Boletas de servicio", "Service tickets",
            "Captura offline en campo por tipo de servicio, firma del cliente, foto-evidencias, folio consecutivo y PDF con la marca de la empresa.",
            "Offline field capture by service type, customer signature, photo evidence, consecutive numbering and branded PDF.",
            "receipt_long", IsCore: false, [Pricing],
            [
                P(Permissions.TicketsRead, "Consultar todas las boletas", "View all tickets"),
                P(Permissions.TicketsReadOwn, "Consultar sus boletas", "View own tickets", "Solo las boletas que capturó o en las que participó.", "Only tickets the user captured or took part in."),
                P(Permissions.TicketsCapture, "Capturar boletas", "Capture tickets"),
                P(Permissions.TicketsSign, "Recabar firma del cliente", "Collect customer signature"),
                P(Permissions.TicketsApprove, "Validar boletas", "Approve tickets", "Revisión de oficina antes de facturar.", "Office review before invoicing."),
                P(Permissions.TicketsCancel, "Cancelar boletas", "Cancel tickets"),
                P(Permissions.TicketsReopen, "Reabrir boletas firmadas", "Reopen signed tickets", "Queda registrado en la bitácora con el motivo.", "Recorded in the audit log with the reason."),
                P(Permissions.TicketsPriceOverride, "Modificar precios en la boleta", "Override ticket prices")
            ]),
        new(Dispatch, 2, ModuleCategory.Operations, "Órdenes de trabajo y programación", "Work orders and dispatch",
            "Qué unidad y qué cuadrilla van a qué pozo y cuándo; la boleta nace de la orden.",
            "Which unit and crew go to which well and when; the ticket is born from the order.",
            "event_note", IsCore: false, [Tickets, Fleet, Crews],
            [
                P(Permissions.DispatchRead, "Consultar todas las órdenes", "View all work orders"),
                P(Permissions.DispatchReadOwn, "Consultar sus órdenes asignadas", "View assigned work orders"),
                P(Permissions.DispatchManage, "Programar y despachar órdenes", "Schedule and dispatch work orders")
            ]),
        new(Pricing, 3, ModuleCategory.Operations, "Catálogo de servicios y tarifarios", "Services and price lists",
            "Conceptos con claves SAT y precios por cliente o contrato: la boleta ya trae el importe correcto.",
            "Concepts with SAT codes and prices per customer or contract: tickets already carry the right amount.",
            "sell", IsCore: false, [],
            [
                P(Permissions.PricingRead, "Consultar servicios y tarifas", "View services and prices"),
                P(Permissions.PricingManage, "Administrar servicios y tarifarios", "Manage services and price lists")
            ]),
        new(Billing, 4, ModuleCategory.Operations, "Tablero de facturación", "Billing board",
            "Servicios ejecutados contra facturados contra cobrados, con alertas de boletas sin facturar.",
            "Executed vs. invoiced vs. collected services, with alerts for uninvoiced tickets.",
            "query_stats", IsCore: false, [Tickets],
            [P(Permissions.BillingRead, "Consultar tablero de facturación", "View billing board")]),

        // ---- Activos y flota -------------------------------------------------------------------
        new(Fleet, 5, ModuleCategory.Assets, "Unidades y equipos", "Units and equipment",
            "Fichas de unidades y equipos: estatus, horómetros, kilometraje e historial por unidad.",
            "Unit and equipment records: status, hour meters, odometer and history per unit.",
            "local_shipping", IsCore: false, [],
            [
                P(Permissions.FleetRead, "Consultar unidades", "View units"),
                P(Permissions.FleetManage, "Administrar unidades", "Manage units"),
                P(Permissions.FleetMeters, "Registrar horómetro y kilometraje", "Record meter readings")
            ]),
        new(Maintenance, 6, ModuleCategory.Assets, "Mantenimiento preventivo y correctivo", "Preventive and corrective maintenance",
            "Programas por horas, kilómetros o días, órdenes de taller, bitácoras y costos.",
            "Plans by hours, kilometers or days, workshop orders, logs and costs.",
            "build", IsCore: false, [Fleet],
            [
                P(Permissions.MaintenanceRead, "Consultar mantenimiento", "View maintenance"),
                P(Permissions.MaintenanceManage, "Administrar mantenimiento", "Manage maintenance")
            ]),
        new(Permits, 7, ModuleCategory.Assets, "Permisos y certificaciones por unidad", "Unit permits and certifications",
            "SCT, SEMARNAT/ASEA, pólizas y verificaciones con semáforo de vencimiento.",
            "SCT, SEMARNAT/ASEA, insurance policies and inspections with expiry traffic lights.",
            "verified", IsCore: false, [Fleet],
            [
                P(Permissions.PermitsRead, "Consultar permisos", "View permits"),
                P(Permissions.PermitsManage, "Administrar permisos", "Manage permits")
            ]),
        new(Fuel, 8, ModuleCategory.Assets, "Control de combustible", "Fuel control",
            "Cargas por unidad contra kilómetros u horas trabajadas y rendimiento fuera de rango.",
            "Loads per unit against kilometers or hours worked, and out-of-range efficiency.",
            "local_gas_station", IsCore: false, [Fleet],
            [
                P(Permissions.FuelRead, "Consultar combustible", "View fuel"),
                P(Permissions.FuelCapture, "Registrar cargas de combustible", "Record fuel loads"),
                P(Permissions.FuelManage, "Administrar combustible", "Manage fuel")
            ]),
        new(Inventory, 9, ModuleCategory.Assets, "Inventario y almacén", "Inventory and warehouse",
            "Refacciones, consumibles y herramienta asignada a cuadrillas.",
            "Spare parts, consumables and tools assigned to crews.",
            "inventory_2", IsCore: false, [],
            [
                P(Permissions.InventoryRead, "Consultar inventario", "View inventory"),
                P(Permissions.InventoryMove, "Registrar movimientos de almacén", "Record stock movements"),
                P(Permissions.InventoryManage, "Administrar artículos y almacenes", "Manage items and warehouses")
            ]),

        // ---- Cumplimiento y personal -----------------------------------------------------------
        new(Personnel, 10, ModuleCategory.Compliance, "Expedientes de personal", "Personnel records",
            "DC-3, H2S, exámenes médicos y cursos SSPA con vencimientos y paquete documental por contrato o cliente.",
            "DC-3, H2S, medical exams and SSPA courses with expiries and document packages per contract or customer.",
            "badge", IsCore: false, [],
            [
                P(Permissions.PersonnelRead, "Consultar expedientes", "View personnel records"),
                P(Permissions.PersonnelManage, "Administrar expedientes", "Manage personnel records"),
                P(Permissions.PersonnelPackages, "Generar paquetes documentales", "Generate document packages")
            ]),
        new(Hse, 11, ModuleCategory.Compliance, "HSE", "HSE",
            "Reporte de incidentes, inspecciones con checklist y pláticas de seguridad (evidencia para auditorías e ISO 45001).",
            "Incident reports, checklist inspections and safety talks (evidence for audits and ISO 45001).",
            "health_and_safety", IsCore: false, [Personnel],
            [
                P(Permissions.HseRead, "Consultar HSE", "View HSE"),
                P(Permissions.HseReport, "Reportar incidentes y registrar inspecciones", "Report incidents and record inspections"),
                P(Permissions.HseManage, "Administrar HSE", "Manage HSE")
            ]),
        new(Crews, 12, ModuleCategory.Compliance, "Cuadrillas y asistencia en campo", "Crews and field attendance",
            "Quién trabajó en qué servicio: insumo para nómina y para el costeo real de cada boleta.",
            "Who worked on which service: input for payroll and for the real cost of each ticket.",
            "groups", IsCore: false, [Personnel],
            [
                P(Permissions.CrewsRead, "Consultar cuadrillas y asistencia", "View crews and attendance"),
                P(Permissions.CrewsManage, "Administrar cuadrillas", "Manage crews"),
                P(Permissions.CrewsAttendance, "Registrar asistencia", "Record attendance")
            ]),
        new(Expenses, 13, ModuleCategory.Compliance, "Viáticos y gastos de campo", "Travel and field expenses",
            "Comprobación con foto del ticket desde la misma app.",
            "Expense reports with a photo of the receipt from the same app.",
            "payments", IsCore: false, [Personnel],
            [
                P(Permissions.ExpensesCapture, "Comprobar sus gastos", "Submit own expenses"),
                P(Permissions.ExpensesRead, "Consultar todas las comprobaciones", "View all expense reports"),
                P(Permissions.ExpensesApprove, "Autorizar comprobaciones", "Approve expense reports")
            ]),

        // ---- Fiscal y comercial ----------------------------------------------------------------
        new(Invoicing, 14, ModuleCategory.Fiscal, "Facturación CFDI 4.0", "CFDI 4.0 invoicing",
            "Integración con un PAC: la boleta firmada se convierte en factura en un clic.",
            "PAC integration: a signed ticket becomes an invoice in one click.",
            "request_quote", IsCore: false, [Tickets],
            [
                P(Permissions.InvoicingRead, "Consultar facturas", "View invoices"),
                P(Permissions.InvoicingIssue, "Emitir y timbrar facturas", "Issue and stamp invoices"),
                P(Permissions.InvoicingCancel, "Cancelar facturas", "Cancel invoices"),
                P(Permissions.InvoicingSettings, "Configurar datos fiscales y PAC", "Configure tax data and PAC")
            ]),
        new(CartaPorte, 15, ModuleCategory.Fiscal, "Carta Porte", "Carta Porte (bill of lading)",
            "Complemento por viaje de autotanque, ligado a unidad, operador y ruta.",
            "Complement per tanker trip, linked to unit, driver and route.",
            "route", IsCore: false, [Invoicing, Fleet, Personnel],
            [
                P(Permissions.CartaPorteRead, "Consultar viajes", "View trips"),
                P(Permissions.CartaPorteManage, "Registrar y timbrar Carta Porte", "Record and stamp Carta Porte")
            ]),
        new(Collections, 16, ModuleCategory.Fiscal, "Cobranza", "Collections",
            "Antigüedad de cartera, estimaciones y COPADEs con Pemex, pagos y recordatorios.",
            "Receivables aging, Pemex estimates and COPADEs, payments and reminders.",
            "account_balance_wallet", IsCore: false, [Invoicing],
            [
                P(Permissions.CollectionsRead, "Consultar cobranza", "View collections"),
                P(Permissions.CollectionsManage, "Administrar cobranza", "Manage collections")
            ]),
        new(Quotes, 17, ModuleCategory.Fiscal, "Cotizaciones", "Quotes",
            "Cotización desde el tarifario que se convierte en orden de trabajo al aceptarse.",
            "Quotes from the price list that become work orders once accepted.",
            "request_page", IsCore: false, [Pricing],
            [
                P(Permissions.QuotesRead, "Consultar cotizaciones", "View quotes"),
                P(Permissions.QuotesManage, "Administrar cotizaciones", "Manage quotes")
            ]),

        // ---- Dirección y clientes --------------------------------------------------------------
        new(Dashboard, 18, ModuleCategory.Management, "Dashboard gerencial", "Management dashboard",
            "Utilización de unidades, ingreso por unidad, días boleta→factura, cartera vencida y servicios del mes.",
            "Unit utilization, revenue per unit, ticket→invoice days, overdue receivables and services of the month.",
            "monitoring", IsCore: false, [Tickets],
            [P(Permissions.DashboardRead, "Consultar dashboard gerencial", "View management dashboard")]),
        new(Portal, 19, ModuleCategory.Management, "Portal del cliente", "Customer portal",
            "La operadora consulta sus boletas, reportes y estatus con la marca de la empresa.",
            "The operator checks its tickets, reports and status under the company brand.",
            "storefront", IsCore: false, [Tickets],
            [P(Permissions.PortalAccess, "Acceso al portal del cliente", "Customer portal access", "Solo ve la información de su cliente.", "Sees only the data of its customer.")]),
        new(Reports, 20, ModuleCategory.Management, "Reportes mensuales", "Monthly reports",
            "Reportes por cliente o campo en PDF y Excel con la marca de la empresa.",
            "Reports per customer or field in PDF and Excel with the company brand.",
            "summarize", IsCore: false, [Tickets],
            [P(Permissions.ReportsRead, "Generar reportes", "Generate reports")]),

        // ---- Plataforma ------------------------------------------------------------------------
        new(Access, 21, ModuleCategory.Platform, "Usuarios, roles y permisos", "Users, roles and permissions",
            "El operador de campo ve solo sus órdenes, la oficina ve todo y el dueño ve el dashboard.",
            "Field operators see only their orders, the office sees everything and the owner sees the dashboard.",
            "admin_panel_settings", IsCore: true, [],
            [
                P(Permissions.AccessUsers, "Administrar usuarios", "Manage users"),
                P(Permissions.AccessRoles, "Administrar roles", "Manage roles"),
                P(Permissions.AccessSettings, "Configurar la empresa", "Configure the company", "Marca, colores, folios y preferencias.", "Brand, colors, numbering and preferences.")
            ]),
        new(Audit, 22, ModuleCategory.Platform, "Bitácora de auditoría", "Audit log",
            "Quién capturó, editó o canceló qué; crítico cuando una boleta respalda una factura.",
            "Who captured, edited or cancelled what; critical when a ticket backs an invoice.",
            "history", IsCore: true, [],
            [P(Permissions.AuditRead, "Consultar bitácora", "View audit log")]),
        new(Notifications, 23, ModuleCategory.Platform, "Notificaciones", "Notifications",
            "WhatsApp y correo: boleta firmada, permiso por vencer, unidad liberada de taller.",
            "WhatsApp and e-mail: ticket signed, permit expiring, unit released from the workshop.",
            "notifications", IsCore: false, [],
            [P(Permissions.NotificationsManage, "Configurar notificaciones", "Configure notifications")]),
        new(Integrations, 24, ModuleCategory.Platform, "Integraciones", "Integrations",
            "Contabilidad (CONTPAQi/Aspel), PAC, GPS de flota y exportación a Excel de todo.",
            "Accounting (CONTPAQi/Aspel), PAC, fleet GPS and Excel export of everything.",
            "hub", IsCore: false, [],
            [
                P(Permissions.IntegrationsManage, "Administrar integraciones y llaves de API", "Manage integrations and API keys"),
                P(Permissions.IntegrationsExport, "Exportar información", "Export data")
            ]),
        new(Automation, 25, ModuleCategory.Platform, "Flujos y reglas de negocio", "Workflows and business rules",
            "Flujos de autorización por pasos y reglas que bloquean o avisan, por empresa o globales para todas.",
            "Step-by-step approval workflows and rules that block or notify, per company or global for all of them.",
            "account_tree", IsCore: true, [],
            [
                P(Permissions.AutomationRead, "Consultar flujos, reglas y autorizaciones", "View workflows, rules and approvals",
                    "Incluye el historial de todas las solicitudes de autorización de la empresa.", "Includes the history of every approval request of the company."),
                P(Permissions.AutomationManage, "Configurar flujos y reglas de la empresa", "Configure the company's workflows and rules",
                    "Las definiciones globales solo las administra la plataforma.", "Global definitions are managed by the platform only.")
            ])
    ];

    private static readonly Dictionary<string, ModuleDefinition> ByCode = All.ToDictionary(m => m.Code, StringComparer.Ordinal);

    private static readonly Dictionary<string, PermissionDefinition> PermissionsByCode =
        All.SelectMany(m => m.Permissions).ToDictionary(p => p.Code, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> CoreModules { get; } = All.Where(m => m.IsCore).Select(m => m.Code).ToArray();

    public static IReadOnlyCollection<string> AllPermissionCodes { get; } = PermissionsByCode.Keys.ToArray();

    public static ModuleDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    public static bool Exists(string code) => ByCode.ContainsKey(code);

    public static bool IsPermission(string code) => PermissionsByCode.ContainsKey(code);

    public static PermissionDefinition? FindPermission(string code) => PermissionsByCode.GetValueOrDefault(code);

    /// <summary>Module of a permission code (<c>tickets.capture</c> → <c>tickets</c>).</summary>
    public static string ModuleOf(string permissionCode)
    {
        var dot = permissionCode.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? permissionCode : permissionCode[..dot];
    }

    /// <summary>Requested modules plus core modules and every transitive dependency.</summary>
    public static IReadOnlyList<string> WithDependencies(IEnumerable<string> modules)
    {
        var result = new HashSet<string>(CoreModules, StringComparer.Ordinal);
        var pending = new Stack<string>(modules.Where(Exists));

        while (pending.Count > 0)
        {
            var code = pending.Pop();
            if (!result.Add(code) && !CoreModules.Contains(code))
            {
                continue;
            }

            foreach (var dependency in ByCode[code].DependsOn)
            {
                if (!result.Contains(dependency))
                {
                    pending.Push(dependency);
                }
            }
        }

        return All.Select(m => m.Code).Where(result.Contains).ToList();
    }

    private static PermissionDefinition P(string code, string name, string nameEn, string? description = null, string? descriptionEn = null) =>
        new(code, name, nameEn, description, descriptionEn);
}
