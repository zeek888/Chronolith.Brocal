namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>
/// Tenant-level permission codes (<c>&lt;module&gt;.&lt;action&gt;</c>). They are granted through the roles each
/// company defines; the ChronoLith Shell only decides who can open Brocal (see <see cref="PlatformPermissions"/>).
/// </summary>
public static class Permissions
{
    public const string CatalogsRead = "catalogs.read";
    public const string CatalogsManage = "catalogs.manage";

    public const string TicketsRead = "tickets.read";
    public const string TicketsReadOwn = "tickets.readown";
    public const string TicketsCapture = "tickets.capture";
    public const string TicketsSign = "tickets.sign";
    public const string TicketsApprove = "tickets.approve";
    public const string TicketsCancel = "tickets.cancel";
    public const string TicketsReopen = "tickets.reopen";
    public const string TicketsPriceOverride = "tickets.priceoverride";

    public const string DispatchRead = "dispatch.read";
    public const string DispatchReadOwn = "dispatch.readown";
    public const string DispatchManage = "dispatch.manage";

    public const string PricingRead = "pricing.read";
    public const string PricingManage = "pricing.manage";

    public const string BillingRead = "billing.read";

    public const string FleetRead = "fleet.read";
    public const string FleetManage = "fleet.manage";
    public const string FleetMeters = "fleet.meters";

    public const string MaintenanceRead = "maintenance.read";
    public const string MaintenanceManage = "maintenance.manage";

    public const string PermitsRead = "permits.read";
    public const string PermitsManage = "permits.manage";

    public const string FuelRead = "fuel.read";
    public const string FuelCapture = "fuel.capture";
    public const string FuelManage = "fuel.manage";

    public const string InventoryRead = "inventory.read";
    public const string InventoryMove = "inventory.move";
    public const string InventoryManage = "inventory.manage";

    public const string PersonnelRead = "personnel.read";
    public const string PersonnelManage = "personnel.manage";
    public const string PersonnelPackages = "personnel.packages";

    public const string HseRead = "hse.read";
    public const string HseReport = "hse.report";
    public const string HseManage = "hse.manage";

    public const string CrewsRead = "crews.read";
    public const string CrewsManage = "crews.manage";
    public const string CrewsAttendance = "crews.attendance";

    public const string ExpensesCapture = "expenses.capture";
    public const string ExpensesRead = "expenses.read";
    public const string ExpensesApprove = "expenses.approve";

    public const string InvoicingRead = "invoicing.read";
    public const string InvoicingIssue = "invoicing.issue";
    public const string InvoicingCancel = "invoicing.cancel";
    public const string InvoicingSettings = "invoicing.settings";

    public const string CartaPorteRead = "cartaporte.read";
    public const string CartaPorteManage = "cartaporte.manage";

    public const string CollectionsRead = "collections.read";
    public const string CollectionsManage = "collections.manage";

    public const string QuotesRead = "quotes.read";
    public const string QuotesManage = "quotes.manage";

    public const string DashboardRead = "dashboard.read";

    public const string PortalAccess = "portal.access";

    public const string ReportsRead = "reports.read";

    public const string AccessUsers = "access.users";
    public const string AccessRoles = "access.roles";
    public const string AccessSettings = "access.settings";

    public const string AuditRead = "audit.read";

    public const string NotificationsManage = "notifications.manage";

    public const string IntegrationsManage = "integrations.manage";
    public const string IntegrationsExport = "integrations.export";

    public const string AutomationRead = "automation.read";
    public const string AutomationManage = "automation.manage";
}

/// <summary>
/// Permissions published to the ChronoLith Shell in the module manifest (Shell RBAC). They only decide who can
/// open Brocal and who administers the platform (companies, quotas, modules); everything else is per company.
/// </summary>
public static class PlatformPermissions
{
    public const string ModuleCode = "brocal";

    public const string Access = "brocal.access";

    public const string ManagePlatform = "brocal.platform.manage";
}

/// <summary>Default roles created for every new company (they can be edited afterwards).</summary>
public static class RoleTemplates
{
    public sealed record Template(string Key, string Name, string Description, IReadOnlyList<string> Permissions);

    public static readonly IReadOnlyList<Template> All =
    [
        new("admin", "Administrador", "Acceso total a la empresa.", BrocalModules.AllPermissionCodes.ToList()),
        new("management", "Dirección", "Dashboard gerencial, reportes y consulta de toda la operación.",
        [
            Permissions.DashboardRead, Permissions.ReportsRead, Permissions.BillingRead, Permissions.CatalogsRead,
            Permissions.TicketsRead, Permissions.DispatchRead, Permissions.PricingRead, Permissions.FleetRead,
            Permissions.MaintenanceRead, Permissions.PermitsRead, Permissions.FuelRead, Permissions.InventoryRead,
            Permissions.PersonnelRead, Permissions.HseRead, Permissions.CrewsRead, Permissions.ExpensesRead,
            Permissions.InvoicingRead, Permissions.CartaPorteRead, Permissions.CollectionsRead, Permissions.QuotesRead,
            Permissions.AuditRead, Permissions.IntegrationsExport, Permissions.AutomationRead
        ]),
        new("office", "Oficina", "Catálogos, tarifas, programación de órdenes, validación de boletas y cotizaciones.",
        [
            Permissions.CatalogsRead, Permissions.CatalogsManage, Permissions.PricingRead, Permissions.PricingManage,
            Permissions.TicketsRead, Permissions.TicketsCapture, Permissions.TicketsApprove, Permissions.TicketsCancel,
            Permissions.DispatchRead, Permissions.DispatchManage, Permissions.BillingRead, Permissions.QuotesRead,
            Permissions.QuotesManage, Permissions.FleetRead, Permissions.PermitsRead, Permissions.PersonnelRead,
            Permissions.CrewsRead, Permissions.ReportsRead, Permissions.InvoicingRead, Permissions.IntegrationsExport
        ]),
        new("supervisor", "Supervisor de campo", "Captura y firma de boletas, asistencia de la cuadrilla y reportes HSE.",
        [
            Permissions.CatalogsRead, Permissions.PricingRead, Permissions.TicketsRead, Permissions.TicketsCapture,
            Permissions.TicketsSign, Permissions.TicketsApprove, Permissions.DispatchRead, Permissions.FleetRead,
            Permissions.FleetMeters, Permissions.CrewsRead, Permissions.CrewsAttendance, Permissions.HseRead,
            Permissions.HseReport, Permissions.FuelCapture, Permissions.ExpensesCapture
        ]),
        new("operator", "Operador de campo", "Ve solo sus órdenes y boletas; captura en campo, combustible y gastos.",
        [
            Permissions.TicketsReadOwn, Permissions.TicketsCapture, Permissions.TicketsSign, Permissions.DispatchReadOwn,
            Permissions.FleetMeters, Permissions.FuelCapture, Permissions.ExpensesCapture, Permissions.HseReport
        ]),
        new("workshop", "Mantenimiento", "Unidades, taller, permisos por unidad y refacciones.",
        [
            Permissions.FleetRead, Permissions.FleetManage, Permissions.FleetMeters, Permissions.MaintenanceRead,
            Permissions.MaintenanceManage, Permissions.PermitsRead, Permissions.PermitsManage, Permissions.FuelRead,
            Permissions.InventoryRead, Permissions.InventoryMove
        ]),
        new("warehouse", "Almacén", "Artículos, existencias, movimientos y herramienta asignada.",
            [Permissions.InventoryRead, Permissions.InventoryMove, Permissions.InventoryManage, Permissions.FleetRead, Permissions.CrewsRead]),
        new("hse", "HSE", "Incidentes, inspecciones, pláticas y expedientes del personal.",
        [
            Permissions.HseRead, Permissions.HseReport, Permissions.HseManage, Permissions.PersonnelRead,
            Permissions.PersonnelManage, Permissions.PersonnelPackages, Permissions.PermitsRead, Permissions.FleetRead,
            Permissions.CrewsRead
        ]),
        new("billing", "Facturación y cobranza", "Facturas CFDI, Carta Porte, cobranza, COPADEs y comprobación de gastos.",
        [
            Permissions.BillingRead, Permissions.InvoicingRead, Permissions.InvoicingIssue, Permissions.InvoicingCancel,
            Permissions.InvoicingSettings, Permissions.CartaPorteRead, Permissions.CartaPorteManage, Permissions.CollectionsRead,
            Permissions.CollectionsManage, Permissions.TicketsRead, Permissions.TicketsApprove, Permissions.CatalogsRead,
            Permissions.PricingRead, Permissions.ReportsRead, Permissions.ExpensesRead, Permissions.ExpensesApprove,
            Permissions.QuotesRead, Permissions.IntegrationsExport
        ]),
        new("customer", "Cliente (portal)", "Personal de la operadora: consulta sus boletas, reportes y estatus.",
            [Permissions.PortalAccess])
    ];

    public static Template? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
