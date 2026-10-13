import { Route, Routes } from '@angular/router';
import { P } from '../core/permissions';
import { platformAdminOnly, requires, staffOnly } from '../core/session';
import { CatalogConfig } from '../shared/crud/fields';
import { ListConfig } from '../shared/crud/list-page';
import { RecordConfig } from '../shared/crud/record-page';
import { AUDIT_LIST, MEMBER_LIST } from './admin/access-pages';
import {
  CONTRACTS,
  CREWS,
  CUSTOMERS,
  DOCUMENT_TYPES,
  EMPLOYEES,
  INSPECTION_TEMPLATES,
  ITEMS,
  MAINTENANCE_PLANS,
  PRICE_LISTS,
  REQUIREMENTS,
  SERVICES,
  SERVICE_TYPES,
  SITES,
  UNITS,
  WAREHOUSES,
} from './catalogs/catalog-configs';
import { QUOTE_LIST, QUOTE_RECORD } from './commercial/quotes.config';
import { MOVEMENT_LIST, STOCK_LIST, TOOL_LIST, documentList } from './compliance/compliance.config';
import { WORK_ORDER_LIST, WORK_ORDER_RECORD } from './dispatch/work-orders.config';
import { COLLECTION_NOTE_LIST, ESTIMATE_LIST, INVOICE_LIST, PAYMENT_LIST, TRIP_LIST, TRIP_RECORD } from './fiscal/fiscal.config';
import { FUEL_EFFICIENCY_LIST, FUEL_LIST, MAINTENANCE_DUE_LIST, MAINTENANCE_ORDER_LIST, MAINTENANCE_ORDER_RECORD } from './fleet/fleet.config';
import { EXPENSE_LIST, EXPENSE_RECORD, INCIDENT_LIST, INCIDENT_RECORD, INSPECTION_LIST, PAYROLL_LIST, TALK_LIST } from './people/people.config';
import { TENANT_LIST } from './platform/platform-pages';
import { TICKET_LIST } from './tickets/tickets.config';

const catalogPage = () => import('../shared/crud/catalog-page').then((m) => m.CatalogPage);
const listPage = () => import('../shared/crud/list-page').then((m) => m.ListPage);
const recordPage = () => import('../shared/crud/record-page').then((m) => m.RecordPage);

/**
 * Master data page guarded by the read permissions of the catalog. Catalogs without a detail page also
 * answer `…/:id`, which opens the record (links from notifications).
 */
function catalog(path: string, config: CatalogConfig, read: string[]): Route[] {
  const base = { canActivate: [requires(...read)], loadComponent: catalogPage, data: { catalog: config }, title: `brocal.nav.${config.key}` };
  return config.detail ? [{ path, ...base }] : [{ path, ...base }, { path: `${path}/:id`, ...base }];
}

function list(path: string, config: ListConfig<never>, read: string[]): Route {
  return { path, canActivate: [requires(...read)], loadComponent: listPage, data: { list: config }, title: `brocal.nav.${config.key}` };
}

/** New record (`…/nuevo`) and record page (`…/:id`) of a document with a life cycle. */
function record(path: string, config: RecordConfig, read: string[], create: string[], newSegment = 'nueva'): Route[] {
  return [
    { path: `${path}/${newSegment}`, canActivate: [requires(...create)], loadComponent: recordPage, data: { record: config }, title: `brocal.records.${config.key}New` },
    { path: `${path}/:id`, canActivate: [requires(...read)], loadComponent: recordPage, data: { record: config }, title: `brocal.records.${config.key}` },
  ];
}

const TICKETS_READ = [P.ticketsRead, P.ticketsReadOwn, P.portalAccess];
const DISPATCH_READ = [P.dispatchRead, P.dispatchReadOwn];
const INVOICES_READ = [P.invoicingRead, P.invoicingIssue, P.collectionsRead, P.portalAccess];
const COLLECTIONS = [P.collectionsRead, P.collectionsManage];

/** Every page of Brocal (children of the layout). Paths are in Spanish, like the links sent by the API. */
export const FEATURE_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'inicio' },
  { path: 'inicio', title: 'brocal.nav.home', loadComponent: () => import('./home/home').then((m) => m.HomePage) },
  { path: 'autorizaciones', title: 'brocal.nav.approvals', canActivate: [staffOnly], loadComponent: () => import('./automation/approvals-page').then((m) => m.ApprovalsPage) },

  // ---- Núcleo operativo ----
  list('boletas', TICKET_LIST as ListConfig<never>, TICKETS_READ),
  { path: 'boletas/nueva', title: 'brocal.tickets.newTitle', canActivate: [requires(P.ticketsCapture)], loadComponent: () => import('./tickets/ticket-capture').then((m) => m.TicketCapturePage) },
  { path: 'boletas/pendientes', title: 'brocal.tickets.pending', canActivate: [requires(P.ticketsCapture)], loadComponent: () => import('./tickets/pending-tickets').then((m) => m.PendingTicketsPage) },
  { path: 'boletas/:id', title: 'brocal.records.ticket', canActivate: [requires(...TICKETS_READ)], loadComponent: () => import('./tickets/ticket-detail').then((m) => m.TicketDetailPage) },
  { path: 'boletas/:id/editar', title: 'brocal.records.ticket', canActivate: [requires(P.ticketsCapture)], loadComponent: () => import('./tickets/ticket-capture').then((m) => m.TicketCapturePage) },

  list('ordenes', WORK_ORDER_LIST as ListConfig<never>, DISPATCH_READ),
  { path: 'ordenes/programacion', title: 'brocal.nav.dispatchBoard', canActivate: [requires(P.dispatchRead, P.dispatchManage)], loadComponent: () => import('./dispatch/dispatch-board').then((m) => m.DispatchBoardPage) },
  { path: 'ordenes/nueva', title: 'brocal.records.workOrderNew', canActivate: [requires(P.dispatchManage)], loadComponent: recordPage, data: { record: WORK_ORDER_RECORD } },
  { path: 'ordenes/:id', title: 'brocal.records.workOrder', canActivate: [requires(...DISPATCH_READ)], loadComponent: () => import('./dispatch/work-order-detail').then((m) => m.WorkOrderDetailPage) },
  { path: 'ordenes/:id/editar', title: 'brocal.records.workOrder', canActivate: [requires(P.dispatchManage)], loadComponent: recordPage, data: { record: WORK_ORDER_RECORD } },

  ...catalog('catalogos/clientes', CUSTOMERS, [P.catalogsRead, P.catalogsManage]),
  ...catalog('catalogos/contratos', CONTRACTS, [P.catalogsRead, P.catalogsManage]),
  ...catalog('catalogos/locaciones', SITES, [P.catalogsRead, P.catalogsManage]),
  ...catalog('tarifas/servicios', SERVICES, [P.pricingRead, P.pricingManage]),
  ...catalog('tarifas/listas', PRICE_LISTS, [P.pricingRead, P.pricingManage]),
  ...catalog('tarifas/tipos-servicio', SERVICE_TYPES, [P.pricingRead, P.pricingManage]),
  { path: 'facturacion/tablero', title: 'brocal.nav.billingBoard', canActivate: [requires(P.billingRead, P.dashboardRead)], loadComponent: () => import('./fiscal/billing-board').then((m) => m.BillingBoardPage) },

  // ---- Activos y flota ----
  ...catalog('flota/unidades', UNITS, [P.fleetRead, P.fleetManage]),
  { path: 'flota/unidades/:id', title: 'brocal.nav.units', canActivate: [requires(P.fleetRead, P.fleetManage, P.maintenanceRead, P.permitsRead, P.fuelRead, P.dispatchManage)], loadComponent: () => import('./fleet/unit-detail').then((m) => m.UnitDetailPage) },
  ...catalog('mantenimiento/programas', MAINTENANCE_PLANS, [P.maintenanceRead, P.maintenanceManage]),
  list('mantenimiento/vencimientos', MAINTENANCE_DUE_LIST as ListConfig<never>, [P.maintenanceRead, P.maintenanceManage]),
  list('mantenimiento/ordenes', MAINTENANCE_ORDER_LIST as ListConfig<never>, [P.maintenanceRead, P.maintenanceManage]),
  ...record('mantenimiento/ordenes', MAINTENANCE_ORDER_RECORD, [P.maintenanceRead, P.maintenanceManage], [P.maintenanceManage]),
  list('permisos/unidades', documentList('unit') as ListConfig<never>, [P.permitsRead, P.permitsManage]),
  { path: 'permisos/matriz', title: 'brocal.nav.unitMatrix', data: { subject: 'unit' }, canActivate: [requires(P.permitsRead, P.permitsManage)], loadComponent: () => import('./compliance/compliance-pages').then((m) => m.ComplianceMatrixPage) },
  ...catalog('permisos/tipos', DOCUMENT_TYPES, [P.permitsRead, P.permitsManage, P.personnelRead, P.personnelManage]),
  list('combustible/cargas', FUEL_LIST as ListConfig<never>, [P.fuelRead, P.fuelCapture, P.fuelManage]),
  list('combustible/rendimiento', FUEL_EFFICIENCY_LIST as ListConfig<never>, [P.fuelRead, P.fuelManage]),
  list('inventario/existencias', STOCK_LIST as ListConfig<never>, [P.inventoryRead, P.inventoryMove, P.inventoryManage]),
  list('inventario/movimientos', MOVEMENT_LIST as ListConfig<never>, [P.inventoryRead, P.inventoryMove, P.inventoryManage]),
  list('inventario/herramienta', TOOL_LIST as ListConfig<never>, [P.inventoryRead, P.inventoryMove, P.inventoryManage]),
  ...catalog('inventario/articulos', ITEMS, [P.inventoryRead, P.inventoryManage]),
  ...catalog('inventario/almacenes', WAREHOUSES, [P.inventoryRead, P.inventoryManage]),

  // ---- Cumplimiento y personal ----
  ...catalog('personal/empleados', EMPLOYEES, [P.personnelRead, P.personnelManage]),
  { path: 'personal/empleados/:id', title: 'brocal.nav.employees', canActivate: [requires(P.personnelRead, P.personnelManage)], loadComponent: () => import('./people/people-pages').then((m) => m.EmployeeDetailPage) },
  list('personal/documentos', documentList('employee') as ListConfig<never>, [P.personnelRead, P.personnelManage]),
  { path: 'personal/matriz', title: 'brocal.nav.personnelMatrix', data: { subject: 'employee' }, canActivate: [requires(P.personnelRead, P.personnelManage, P.personnelPackages)], loadComponent: () => import('./compliance/compliance-pages').then((m) => m.ComplianceMatrixPage) },
  { path: 'personal/paquetes', title: 'brocal.nav.packages', canActivate: [requires(P.personnelPackages, P.permitsManage, P.personnelManage)], loadComponent: () => import('./compliance/compliance-pages').then((m) => m.PackagesPage) },
  ...catalog('personal/requisitos', REQUIREMENTS, [P.personnelRead, P.personnelManage, P.personnelPackages, P.permitsRead]),
  list('hse/incidentes', INCIDENT_LIST as ListConfig<never>, [P.hseRead, P.hseReport, P.hseManage]),
  ...record('hse/incidentes', INCIDENT_RECORD, [P.hseRead, P.hseReport, P.hseManage], [P.hseReport, P.hseManage], 'nuevo'),
  list('hse/inspecciones', INSPECTION_LIST as ListConfig<never>, [P.hseRead, P.hseReport, P.hseManage]),
  { path: 'hse/inspecciones/nueva', title: 'brocal.hse.newInspection', canActivate: [requires(P.hseReport, P.hseManage)], loadComponent: () => import('./people/hse-pages').then((m) => m.InspectionRecordPage) },
  { path: 'hse/inspecciones/:id', title: 'brocal.nav.inspections', canActivate: [requires(P.hseRead, P.hseReport, P.hseManage)], loadComponent: () => import('./people/hse-pages').then((m) => m.InspectionDetailPage) },
  list('hse/platicas', TALK_LIST as ListConfig<never>, [P.hseRead, P.hseReport, P.hseManage]),
  { path: 'hse/indicadores', title: 'brocal.nav.hseStats', canActivate: [requires(P.hseRead, P.hseManage, P.dashboardRead)], loadComponent: () => import('./people/hse-pages').then((m) => m.HseStatsPage) },
  ...catalog('hse/plantillas', INSPECTION_TEMPLATES, [P.hseRead, P.hseManage]),
  // Before the crews catalog: its `cuadrillas/:id` would take "asistencia" and "prenomina" as record ids.
  { path: 'cuadrillas/asistencia', title: 'brocal.nav.attendance', canActivate: [requires(P.crewsAttendance, P.crewsRead, P.crewsManage)], loadComponent: () => import('./people/people-pages').then((m) => m.AttendancePage) },
  list('cuadrillas/prenomina', PAYROLL_LIST as ListConfig<never>, [P.crewsRead, P.crewsManage]),
  ...catalog('cuadrillas', CREWS, [P.crewsRead, P.crewsManage]),
  list('viaticos', EXPENSE_LIST as ListConfig<never>, [P.expensesCapture, P.expensesRead, P.expensesApprove]),
  ...record('viaticos', EXPENSE_RECORD, [P.expensesCapture, P.expensesRead, P.expensesApprove], [P.expensesCapture, P.expensesApprove], 'nuevo'),

  // ---- Fiscal y comercial ----
  list('facturacion/facturas', INVOICE_LIST as ListConfig<never>, INVOICES_READ),
  { path: 'facturacion/nueva', title: 'brocal.nav.invoiceTickets', canActivate: [requires(P.invoicingIssue)], loadComponent: () => import('./fiscal/invoice-tickets').then((m) => m.InvoiceTicketsPage) },
  { path: 'facturacion/facturas/:id', title: 'brocal.nav.invoices', canActivate: [requires(...INVOICES_READ)], loadComponent: () => import('./fiscal/invoice-detail').then((m) => m.InvoiceDetailPage) },
  { path: 'cobranza/facturas/:id', redirectTo: 'facturacion/facturas/:id' },
  { path: 'cobranza/antiguedad', title: 'brocal.nav.aging', canActivate: [requires(...COLLECTIONS, P.dashboardRead)], loadComponent: () => import('./fiscal/billing-board').then((m) => m.AgingPage) },
  list('cobranza/pagos', PAYMENT_LIST as ListConfig<never>, COLLECTIONS),
  list('cobranza/estimaciones', ESTIMATE_LIST as ListConfig<never>, [...COLLECTIONS, P.invoicingRead]),
  list('cobranza/gestion', COLLECTION_NOTE_LIST as ListConfig<never>, COLLECTIONS),
  list('cartaporte', TRIP_LIST as ListConfig<never>, [P.cartaPorteRead, P.cartaPorteManage]),
  ...record('cartaporte', TRIP_RECORD, [P.cartaPorteRead, P.cartaPorteManage], [P.cartaPorteManage], 'nuevo'),
  list('cotizaciones', QUOTE_LIST as ListConfig<never>, [P.quotesRead, P.quotesManage]),
  ...record('cotizaciones', QUOTE_RECORD, [P.quotesRead, P.quotesManage], [P.quotesManage]),

  // ---- Dirección y clientes ----
  { path: 'dashboard', title: 'brocal.nav.dashboard', canActivate: [requires(P.dashboardRead)], loadComponent: () => import('./management/management-pages').then((m) => m.DashboardPage) },
  { path: 'reportes', title: 'brocal.nav.reports', canActivate: [requires(P.reportsRead, P.portalAccess)], loadComponent: () => import('./management/management-pages').then((m) => m.ReportsPage) },
  { path: 'portal', title: 'brocal.nav.portal', canActivate: [requires(P.portalAccess, P.catalogsRead)], loadComponent: () => import('./management/management-pages').then((m) => m.PortalPage) },

  // ---- Plataforma ----
  { path: 'configuracion/empresa', title: 'brocal.nav.company', canActivate: [requires(P.accessSettings, P.invoicingSettings)], loadComponent: () => import('./admin/company-page').then((m) => m.CompanyPage) },
  list('configuracion/usuarios', MEMBER_LIST as ListConfig<never>, [P.accessUsers]),
  { path: 'configuracion/roles', title: 'brocal.nav.roles', canActivate: [requires(P.accessRoles)], loadComponent: () => import('./admin/access-pages').then((m) => m.RolesPage) },
  { path: 'configuracion/notificaciones', title: 'brocal.nav.notificationSettings', canActivate: [requires(P.notificationsManage)], loadComponent: () => import('./admin/settings-pages').then((m) => m.NotificationSettingsPage) },
  { path: 'configuracion/integraciones', title: 'brocal.nav.integrations', canActivate: [requires(P.integrationsManage, P.integrationsExport)], loadComponent: () => import('./admin/settings-pages').then((m) => m.IntegrationsPage) },
  list('configuracion/bitacora', AUDIT_LIST as ListConfig<never>, [P.auditRead]),
  { path: 'configuracion/flujos', title: 'brocal.nav.workflows', canActivate: [requires(P.automationRead, P.automationManage)], loadComponent: () => import('./automation/workflows-pages').then((m) => m.WorkflowsPage) },
  { path: 'configuracion/flujos/nuevo', title: 'brocal.workflows.new', canActivate: [requires(P.automationManage)], loadComponent: () => import('./automation/workflows-pages').then((m) => m.WorkflowEditorPage) },
  { path: 'configuracion/flujos/:id', title: 'brocal.nav.workflows', canActivate: [requires(P.automationRead, P.automationManage)], loadComponent: () => import('./automation/workflows-pages').then((m) => m.WorkflowEditorPage) },
  { path: 'configuracion/reglas', title: 'brocal.nav.businessRules', canActivate: [requires(P.automationRead, P.automationManage)], loadComponent: () => import('./automation/rules-pages').then((m) => m.RulesPage) },
  { path: 'configuracion/reglas/nueva', title: 'brocal.rules.new', canActivate: [requires(P.automationManage)], loadComponent: () => import('./automation/rules-pages').then((m) => m.RuleEditorPage) },
  { path: 'configuracion/reglas/:id', title: 'brocal.nav.businessRules', canActivate: [requires(P.automationRead, P.automationManage)], loadComponent: () => import('./automation/rules-pages').then((m) => m.RuleEditorPage) },
  { ...list('plataforma/empresas', TENANT_LIST as ListConfig<never>, []), canActivate: [platformAdminOnly] },
  { path: 'plataforma/empresas/:id', title: 'brocal.nav.tenants', canActivate: [platformAdminOnly], loadComponent: () => import('./platform/platform-pages').then((m) => m.TenantDetailPage) },
  { path: 'plataforma/tareas', title: 'brocal.nav.scheduledJobs', canActivate: [platformAdminOnly], loadComponent: () => import('./platform/jobs-page').then((m) => m.ScheduledJobsPage) },

  { path: '**', redirectTo: 'inicio' },
];
