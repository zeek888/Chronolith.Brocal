import { P } from './permissions';

export interface MenuItem {
  /** Translation key under `nav.`. */
  key: string;
  icon: string;
  path: string;
  /** Any of these permissions (empty = every member of the company). */
  permissions: readonly string[];
  /** Module that must be contracted by the company. */
  module?: string;
  /** Only for platform administrators. */
  platformAdmin?: boolean;
}

export interface MenuGroup {
  key: string;
  icon: string;
  items: MenuItem[];
}

/** The 24 modules grouped as in the catalog: operations, assets, compliance, fiscal, management and platform. */
export const MENU: MenuGroup[] = [
  {
    key: 'operations',
    icon: 'receipt_long',
    items: [
      { key: 'home', icon: 'space_dashboard', path: 'inicio', permissions: [] },
      { key: 'tickets', icon: 'receipt_long', path: 'boletas', permissions: [P.ticketsRead, P.ticketsReadOwn, P.portalAccess], module: 'tickets' },
      { key: 'workOrders', icon: 'event_note', path: 'ordenes', permissions: [P.dispatchRead, P.dispatchReadOwn], module: 'dispatch' },
      { key: 'dispatchBoard', icon: 'view_timeline', path: 'ordenes/programacion', permissions: [P.dispatchRead, P.dispatchManage], module: 'dispatch' },
      { key: 'customers', icon: 'apartment', path: 'catalogos/clientes', permissions: [P.catalogsRead, P.catalogsManage] },
      { key: 'contracts', icon: 'handshake', path: 'catalogos/contratos', permissions: [P.catalogsRead, P.catalogsManage] },
      { key: 'sites', icon: 'oil_barrel', path: 'catalogos/locaciones', permissions: [P.catalogsRead, P.catalogsManage] },
      { key: 'services', icon: 'design_services', path: 'tarifas/servicios', permissions: [P.pricingRead, P.pricingManage], module: 'pricing' },
      { key: 'priceLists', icon: 'request_quote', path: 'tarifas/listas', permissions: [P.pricingRead, P.pricingManage], module: 'pricing' },
      { key: 'serviceTypes', icon: 'dynamic_form', path: 'tarifas/tipos-servicio', permissions: [P.pricingRead, P.pricingManage], module: 'pricing' },
      { key: 'billingBoard', icon: 'query_stats', path: 'facturacion/tablero', permissions: [P.billingRead], module: 'billing' },
    ],
  },
  {
    key: 'assets',
    icon: 'local_shipping',
    items: [
      { key: 'units', icon: 'local_shipping', path: 'flota/unidades', permissions: [P.fleetRead, P.fleetManage], module: 'fleet' },
      { key: 'maintenanceOrders', icon: 'build', path: 'mantenimiento/ordenes', permissions: [P.maintenanceRead, P.maintenanceManage], module: 'maintenance' },
      { key: 'maintenanceDue', icon: 'pending_actions', path: 'mantenimiento/vencimientos', permissions: [P.maintenanceRead, P.maintenanceManage], module: 'maintenance' },
      { key: 'maintenancePlans', icon: 'event_repeat', path: 'mantenimiento/programas', permissions: [P.maintenanceRead, P.maintenanceManage], module: 'maintenance' },
      { key: 'unitPermits', icon: 'verified', path: 'permisos/unidades', permissions: [P.permitsRead, P.permitsManage], module: 'permits' },
      { key: 'unitMatrix', icon: 'grid_on', path: 'permisos/matriz', permissions: [P.permitsRead, P.permitsManage], module: 'permits' },
      { key: 'fuelLoads', icon: 'local_gas_station', path: 'combustible/cargas', permissions: [P.fuelRead, P.fuelCapture, P.fuelManage], module: 'fuel' },
      { key: 'fuelEfficiency', icon: 'speed', path: 'combustible/rendimiento', permissions: [P.fuelRead, P.fuelManage], module: 'fuel' },
      { key: 'stock', icon: 'inventory_2', path: 'inventario/existencias', permissions: [P.inventoryRead, P.inventoryMove, P.inventoryManage], module: 'inventory' },
      { key: 'movements', icon: 'swap_horiz', path: 'inventario/movimientos', permissions: [P.inventoryRead, P.inventoryMove, P.inventoryManage], module: 'inventory' },
      { key: 'tools', icon: 'construction', path: 'inventario/herramienta', permissions: [P.inventoryRead, P.inventoryMove, P.inventoryManage], module: 'inventory' },
      { key: 'items', icon: 'category', path: 'inventario/articulos', permissions: [P.inventoryRead, P.inventoryManage], module: 'inventory' },
      { key: 'warehouses', icon: 'warehouse', path: 'inventario/almacenes', permissions: [P.inventoryRead, P.inventoryManage], module: 'inventory' },
    ],
  },
  {
    key: 'compliance',
    icon: 'health_and_safety',
    items: [
      { key: 'employees', icon: 'badge', path: 'personal/empleados', permissions: [P.personnelRead, P.personnelManage], module: 'personnel' },
      { key: 'personnelDocuments', icon: 'workspace_premium', path: 'personal/documentos', permissions: [P.personnelRead, P.personnelManage], module: 'personnel' },
      { key: 'personnelMatrix', icon: 'grid_on', path: 'personal/matriz', permissions: [P.personnelRead, P.personnelManage, P.personnelPackages], module: 'personnel' },
      { key: 'packages', icon: 'folder_zip', path: 'personal/paquetes', permissions: [P.personnelPackages, P.permitsManage, P.personnelManage], module: 'personnel' },
      { key: 'requirements', icon: 'checklist', path: 'personal/requisitos', permissions: [P.personnelRead, P.personnelManage, P.personnelPackages, P.permitsRead] },
      { key: 'documentTypes', icon: 'description', path: 'permisos/tipos', permissions: [P.permitsRead, P.permitsManage, P.personnelRead, P.personnelManage] },
      { key: 'incidents', icon: 'report', path: 'hse/incidentes', permissions: [P.hseRead, P.hseReport, P.hseManage], module: 'hse' },
      { key: 'inspections', icon: 'fact_check', path: 'hse/inspecciones', permissions: [P.hseRead, P.hseReport, P.hseManage], module: 'hse' },
      { key: 'safetyTalks', icon: 'record_voice_over', path: 'hse/platicas', permissions: [P.hseRead, P.hseReport, P.hseManage], module: 'hse' },
      { key: 'hseStats', icon: 'monitor_heart', path: 'hse/indicadores', permissions: [P.hseRead, P.hseManage], module: 'hse' },
      { key: 'inspectionTemplates', icon: 'rule', path: 'hse/plantillas', permissions: [P.hseRead, P.hseManage], module: 'hse' },
      { key: 'crews', icon: 'groups', path: 'cuadrillas', permissions: [P.crewsRead, P.crewsManage], module: 'crews' },
      { key: 'attendance', icon: 'how_to_reg', path: 'cuadrillas/asistencia', permissions: [P.crewsAttendance, P.crewsRead, P.crewsManage], module: 'crews' },
      { key: 'payroll', icon: 'payments', path: 'cuadrillas/prenomina', permissions: [P.crewsRead, P.crewsManage], module: 'crews' },
      { key: 'expenses', icon: 'receipt', path: 'viaticos', permissions: [P.expensesCapture, P.expensesRead, P.expensesApprove], module: 'expenses' },
    ],
  },
  {
    key: 'fiscal',
    icon: 'account_balance',
    items: [
      { key: 'invoices', icon: 'request_page', path: 'facturacion/facturas', permissions: [P.invoicingRead, P.invoicingIssue, P.collectionsRead, P.portalAccess], module: 'invoicing' },
      { key: 'invoiceTickets', icon: 'post_add', path: 'facturacion/nueva', permissions: [P.invoicingIssue], module: 'invoicing' },
      { key: 'cartaPorte', icon: 'route', path: 'cartaporte', permissions: [P.cartaPorteRead, P.cartaPorteManage], module: 'cartaporte' },
      { key: 'aging', icon: 'hourglass_bottom', path: 'cobranza/antiguedad', permissions: [P.collectionsRead, P.collectionsManage], module: 'collections' },
      { key: 'payments', icon: 'paid', path: 'cobranza/pagos', permissions: [P.collectionsRead, P.collectionsManage], module: 'collections' },
      { key: 'estimates', icon: 'assignment_turned_in', path: 'cobranza/estimaciones', permissions: [P.collectionsRead, P.collectionsManage], module: 'collections' },
      { key: 'collectionNotes', icon: 'support_agent', path: 'cobranza/gestion', permissions: [P.collectionsRead, P.collectionsManage], module: 'collections' },
      { key: 'quotes', icon: 'request_quote', path: 'cotizaciones', permissions: [P.quotesRead, P.quotesManage], module: 'quotes' },
    ],
  },
  {
    key: 'management',
    icon: 'monitoring',
    items: [
      { key: 'dashboard', icon: 'monitoring', path: 'dashboard', permissions: [P.dashboardRead], module: 'dashboard' },
      { key: 'reports', icon: 'summarize', path: 'reportes', permissions: [P.reportsRead, P.portalAccess], module: 'reports' },
      { key: 'portal', icon: 'storefront', path: 'portal', permissions: [P.portalAccess], module: 'portal' },
    ],
  },
  {
    key: 'platform',
    icon: 'settings',
    items: [
      { key: 'company', icon: 'business', path: 'configuracion/empresa', permissions: [P.accessSettings, P.invoicingSettings] },
      { key: 'users', icon: 'group', path: 'configuracion/usuarios', permissions: [P.accessUsers] },
      { key: 'roles', icon: 'admin_panel_settings', path: 'configuracion/roles', permissions: [P.accessRoles] },
      { key: 'notificationSettings', icon: 'notifications_active', path: 'configuracion/notificaciones', permissions: [P.notificationsManage], module: 'notifications' },
      { key: 'integrations', icon: 'hub', path: 'configuracion/integraciones', permissions: [P.integrationsManage, P.integrationsExport], module: 'integrations' },
      { key: 'audit', icon: 'history', path: 'configuracion/bitacora', permissions: [P.auditRead] },
      { key: 'tenants', icon: 'domain_add', path: 'plataforma/empresas', permissions: [], platformAdmin: true },
    ],
  },
];
