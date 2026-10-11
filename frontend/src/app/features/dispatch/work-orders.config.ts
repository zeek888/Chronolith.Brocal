import { firstValueFrom } from 'rxjs';
import { fromLocalInput, nowLocalInput, toLocalInput } from '../../core/format';
import { Lookup, PlannedServiceInfo, WorkOrderSummary } from '../../core/models';
import { P } from '../../core/permissions';
import { FieldDef } from '../../shared/crud/fields';
import { ListConfig } from '../../shared/crud/list-page';
import { RecordConfig } from '../../shared/crud/record-page';

const STATUSES = ['scheduled', 'dispatched', 'inProgress', 'completed', 'cancelled'] as const;
const PRIORITIES = ['low', 'normal', 'high', 'urgent'] as const;

export const WORK_ORDER_LIST: ListConfig<WorkOrderSummary> = {
  key: 'workOrders',
  icon: 'event_note',
  api: 'work-orders',
  link: (r) => ['ordenes', r.id],
  create: { permissions: [P.dispatchManage], path: 'ordenes/nueva', label: 'workOrders.new' },
  headerActions: [{ key: 'board', icon: 'view_timeline', label: 'nav.dispatchBoard', path: 'ordenes/programacion', permissions: [P.dispatchRead, P.dispatchManage] }],
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'workOrderStatus', values: STATUSES, multiple: true },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.serviceTypeName },
    { key: 'scheduledStart', label: 'fields.scheduledStart', type: 'datetime' },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r.siteName },
    { key: 'description', label: 'fields.description', wide: true },
    { key: 'crewName', label: 'fields.crew', wide: true },
    { key: 'priority', label: 'fields.priority', type: 'status', enum: 'workOrderPriority', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'workOrderStatus' },
  ],
  muted: (r) => r.status === 'cancelled',
};

const fields: FieldDef[] = [
  { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', required: true },
  { key: 'contractId', label: 'fields.contract', type: 'lookup', catalog: 'catalogs/contracts', parent: 'customerId' },
  { key: 'siteId', label: 'fields.site', type: 'lookup', catalog: 'catalogs/sites', parent: 'customerId', required: true },
  { key: 'serviceTypeId', label: 'fields.serviceType', type: 'lookup', catalog: 'pricing/service-types', required: true },
  { key: 'priority', label: 'fields.priority', type: 'enum', enum: 'workOrderPriority', values: PRIORITIES, required: true },
  { key: 'customerReference', label: 'fields.customerReference', type: 'text', hint: 'fields.customerReferenceHint' },
  { key: 'scheduledStart', label: 'fields.scheduledStart', type: 'datetime', required: true },
  { key: 'scheduledEnd', label: 'fields.scheduledEnd', type: 'datetime', required: true },
  { key: 'description', label: 'fields.workToDo', type: 'textarea', required: true, maxLength: 1000 },
  { key: 'resources', label: 'workOrders.resources', type: 'section' },
  { key: 'unitIds', label: 'fields.units', type: 'lookups', catalog: 'fleet/units', span: 'full' },
  { key: 'crewId', label: 'fields.crew', type: 'lookup', catalog: 'crews' },
  { key: 'employeeIds', label: 'fields.personnel', type: 'lookups', catalog: 'personnel/employees', span: 'full', hint: 'workOrders.personnelHint' },
  {
    key: 'plannedServices',
    label: 'workOrders.plannedServices',
    type: 'array',
    addLabel: 'fields.addService',
    fields: [
      { key: 'serviceId', label: 'fields.service', type: 'lookup', catalog: 'pricing/services', required: true },
      { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0 },
    ],
  },
  { key: 'instructions', label: 'fields.instructions', type: 'textarea', maxLength: 2000 },
];

/** Form of a work order (new and edit); the detail page shows its tickets and status actions. */
export const WORK_ORDER_RECORD: RecordConfig = {
  key: 'workOrder',
  icon: 'event_note',
  api: 'work-orders',
  listPath: 'ordenes',
  manage: [P.dispatchManage],
  fields,
  editable: (r) => {
    const status = (r['summary'] as WorkOrderSummary | undefined)?.status;
    return status !== 'completed' && status !== 'cancelled';
  },
  defaults: async (query, ctx) => {
    const start = nowLocalInput(24).slice(0, 11) + '07:00';
    const value: Record<string, unknown> = { priority: 'normal', scheduledStart: start, scheduledEnd: start.slice(0, 11) + '17:00', unitIds: [], employeeIds: [], plannedServices: [] };
    const unitId = query.get('unidad');
    if (unitId) {
      value['unitIds'] = [unitId];
    }

    const customers = await firstValueFrom(ctx.api.get<Lookup[]>('catalogs/customers/lookup', { activeOnly: true })).catch(() => [] as Lookup[]);
    if (customers.length === 1) {
      value['customerId'] = customers[0].id;
    }

    return value;
  },
  toForm: (r) => {
    const summary = r['summary'] as WorkOrderSummary;
    return {
      customerId: summary.customerId,
      contractId: r['contractId'],
      siteId: summary.siteId,
      serviceTypeId: summary.serviceTypeId,
      priority: summary.priority,
      customerReference: summary.customerReference,
      scheduledStart: toLocalInput(summary.scheduledStart),
      scheduledEnd: toLocalInput(summary.scheduledEnd),
      description: summary.description,
      unitIds: summary.unitIds,
      crewId: summary.crewId,
      employeeIds: ((r['employees'] as { id: string }[]) ?? []).map((e) => e.id),
      plannedServices: ((r['plannedServices'] as PlannedServiceInfo[]) ?? []).map((p) => ({ serviceId: p.serviceId, quantity: p.quantity })),
      instructions: r['instructions'],
    };
  },
  toPayload: (v) => ({
    data: { ...v, scheduledStart: fromLocalInput(v['scheduledStart'] as string), scheduledEnd: fromLocalInput(v['scheduledEnd'] as string), unitIds: v['unitIds'] ?? [], employeeIds: v['employeeIds'] ?? [] },
    dispatch: false,
  }),
  title: (r) => (r['summary'] as WorkOrderSummary).folio,
  status: { enum: 'workOrderStatus', value: (r) => (r['summary'] as WorkOrderSummary).status },
  warnings: (r) => (r['warnings'] as string[]) ?? [],
};
