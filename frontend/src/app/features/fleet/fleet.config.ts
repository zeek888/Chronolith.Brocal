import { firstValueFrom } from 'rxjs';
import { firstOfMonth, nowLocalInput, toLocalInput, today } from '../../core/format';
import { FuelEfficiencyRow, FuelLoad, MaintenanceDue, MaintenanceOrderDetail, MaintenanceOrderSummary } from '../../core/models';
import { P } from '../../core/permissions';
import { FieldDef } from '../../shared/crud/fields';
import { DialogSpec, ListConfig } from '../../shared/crud/list-page';
import { RecordConfig } from '../../shared/crud/record-page';

// ---- Maintenance -------------------------------------------------------------------------------------------

export const MAINTENANCE_DUE_LIST: ListConfig<MaintenanceDue> = {
  key: 'maintenanceDue',
  icon: 'pending_actions',
  api: 'maintenance/due',
  paged: false,
  link: (r) => ['flota/unidades', r.unitId],
  filters: [{ key: 'onlyDue', label: 'maintenance.onlyDue', type: 'toggle', default: true }],
  columns: [
    { key: 'unitCode', label: 'fields.unit', sub: (r) => r.unitName },
    { key: 'planName', label: 'fields.plan', sub: (r, t) => t(`enums.maintenanceTrigger.${r.trigger}`) },
    { key: 'unitStatus', label: 'fields.unitStatus', type: 'status', enum: 'unitStatus', wide: true },
    { key: 'nextAt', label: 'maintenance.nextAt', value: (r) => r.nextOn ?? r.nextAt, align: 'end' },
    { key: 'remaining', label: 'maintenance.remaining', type: 'number', align: 'end' },
    {
      key: 'isOverdue',
      label: 'fields.status',
      value: (r, t) => (r.isOverdue ? `⛔ ${t('maintenance.overdue')}` : r.isDue ? `⚠ ${t('maintenance.due')}` : `✔ ${t('maintenance.upToDate')}`),
      sub: (r, t) => (r.hasOpenOrder ? t('maintenance.openOrder') : null),
    },
  ],
  rowActions: [
    {
      key: 'order',
      icon: 'build',
      label: 'maintenance.createOrder',
      permissions: [P.maintenanceManage],
      visible: (r) => !r.hasOpenOrder,
      run: async (ctx, row) => {
        await ctx.nav.navigate(['mantenimiento/ordenes/nueva'], { queryParams: { unidad: row.unitId, programa: row.planId } });
        return false;
      },
    },
  ],
  muted: (r) => !r.isDue && !r.isOverdue,
};

const ORDER_STATUSES = ['open', 'inProgress', 'waitingParts', 'closed', 'cancelled'] as const;

export const MAINTENANCE_ORDER_LIST: ListConfig<MaintenanceOrderSummary> = {
  key: 'maintenanceOrders',
  icon: 'build',
  api: 'maintenance/orders',
  link: (r) => ['mantenimiento/ordenes', r.id],
  exportDataset: 'maintenance',
  create: { permissions: [P.maintenanceManage], path: 'mantenimiento/ordenes/nueva', label: 'maintenance.newOrder' },
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'maintenanceOrderStatus', values: ORDER_STATUSES },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'maintenanceType', values: ['preventive', 'corrective'] },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.reportedFailure },
    { key: 'unitCode', label: 'fields.unit', sub: (r) => r.unitName },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'maintenanceType' },
    { key: 'openedAt', label: 'maintenance.openedAt', type: 'datetime', wide: true },
    { key: 'closedAt', label: 'maintenance.closedAt', type: 'datetime', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'maintenanceOrderStatus' },
    { key: 'totalCost', label: 'maintenance.totalCost', type: 'money', align: 'end' },
  ],
};

const orderFields: FieldDef[] = [
  { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units', required: true },
  { key: 'type', label: 'fields.type', type: 'enum', enum: 'maintenanceType', values: ['preventive', 'corrective'], required: true },
  { key: 'planId', label: 'fields.plan', type: 'lookup', catalog: 'maintenance/plans', showIf: (v) => v['type'] === 'preventive' },
  { key: 'openedAt', label: 'maintenance.openedAt', type: 'datetime', required: true },
  { key: 'reportedFailure', label: 'maintenance.reportedFailure', type: 'textarea' },
  { key: 'diagnosis', label: 'maintenance.diagnosis', type: 'textarea' },
  {
    key: 'tasks',
    label: 'fields.tasks',
    type: 'array',
    addLabel: 'maintenance.addTask',
    fields: [
      { key: 'description', label: 'fields.description', type: 'text', required: true, span: 'full' },
      { key: 'done', label: 'maintenance.done', type: 'checkbox' },
    ],
  },
  {
    key: 'parts',
    label: 'maintenance.parts',
    type: 'array',
    addLabel: 'maintenance.addPart',
    fields: [
      { key: 'itemId', label: 'fields.item', type: 'lookup', catalog: 'inventory/items', hint: 'maintenance.itemHint' },
      { key: 'warehouseId', label: 'fields.warehouse', type: 'lookup', catalog: 'inventory/warehouses' },
      { key: 'description', label: 'fields.description', type: 'text', required: true },
      { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0 },
      { key: 'unitCost', label: 'fields.unitCost', type: 'money', required: true, min: 0 },
    ],
  },
  { key: 'labor', label: 'maintenance.labor', type: 'section' },
  { key: 'laborHours', label: 'maintenance.laborHours', type: 'number', min: 0, required: true },
  { key: 'laborCostPerHour', label: 'maintenance.laborCostPerHour', type: 'money', required: true },
  { key: 'technicians', label: 'maintenance.technicians', type: 'text' },
  { key: 'externalProvider', label: 'maintenance.externalProvider', type: 'text' },
  { key: 'externalInvoice', label: 'maintenance.externalInvoice', type: 'text' },
  { key: 'externalCost', label: 'maintenance.externalCost', type: 'money', required: true },
  { key: 'notes', label: 'fields.notes', type: 'textarea' },
];

const isOpen = (r: Record<string, unknown>) => {
  const status = (r['summary'] as MaintenanceOrderSummary).status;
  return status === 'open' || status === 'inProgress' || status === 'waitingParts';
};

export const MAINTENANCE_ORDER_RECORD: RecordConfig = {
  key: 'maintenanceOrder',
  icon: 'build',
  api: 'maintenance/orders',
  listPath: 'mantenimiento/ordenes',
  manage: [P.maintenanceManage],
  editable: isOpen,
  fields: orderFields,
  defaults: (query) => ({
    unitId: query.get('unidad'),
    planId: query.get('programa'),
    type: query.get('programa') ? 'preventive' : 'corrective',
    openedAt: nowLocalInput(),
    tasks: [],
    parts: [],
    laborHours: 0,
    laborCostPerHour: 0,
    externalCost: 0,
  }),
  toForm: (r) => {
    const o = r as unknown as MaintenanceOrderDetail;
    return {
      unitId: o.summary.unitId,
      type: o.summary.type,
      planId: o.planId,
      openedAt: toLocalInput(o.summary.openedAt),
      reportedFailure: o.summary.reportedFailure,
      diagnosis: o.diagnosis,
      tasks: o.tasks,
      parts: o.parts.map((p) => ({ itemId: p.itemId, warehouseId: p.warehouseId, description: p.description, quantity: p.quantity, unitCost: p.unitCost })),
      laborHours: o.laborHours,
      laborCostPerHour: o.laborCostPerHour,
      technicians: o.technicians,
      externalProvider: o.externalProvider,
      externalInvoice: o.externalInvoice,
      externalCost: o.externalCost,
      notes: o.notes,
    };
  },
  toPayload: (v) => ({ ...v, openedAt: new Date(v['openedAt'] as string).toISOString(), planId: v['type'] === 'preventive' ? (v['planId'] ?? null) : null }),
  title: (r) => (r['summary'] as MaintenanceOrderSummary).folio,
  subtitle: (r) => {
    const o = r as unknown as MaintenanceOrderDetail;
    return `${o.summary.unitCode} · ${o.summary.unitName}${o.downtimeHours ? ` · ${Math.round(o.downtimeHours)} h fuera de servicio` : ''}`;
  },
  status: { enum: 'maintenanceOrderStatus', value: (r) => (r['summary'] as MaintenanceOrderSummary).status },
  workflow: 'maintenanceOrder',
  warnings: (r) => ((r['cancellationReason'] as string | null) ? [`Cancelada: ${r['cancellationReason'] as string}`] : []),
  actions: [
    {
      key: 'start',
      icon: 'play_circle',
      label: 'maintenance.actions.start',
      permissions: [P.maintenanceManage],
      visible: (r) => (r['summary'] as MaintenanceOrderSummary).status === 'open' || (r['summary'] as MaintenanceOrderSummary).status === 'waitingParts',
      run: async (ctx, r) => {
        await firstValueFrom(ctx.api.post(`maintenance/orders/${r['id'] as string}/status`, { status: 'inProgress' }));
        return true;
      },
    },
    {
      key: 'parts',
      icon: 'inventory',
      label: 'maintenance.actions.waitingParts',
      permissions: [P.maintenanceManage],
      visible: (r) => (r['summary'] as MaintenanceOrderSummary).status === 'inProgress',
      run: async (ctx, r) => {
        await firstValueFrom(ctx.api.post(`maintenance/orders/${r['id'] as string}/status`, { status: 'waitingParts' }));
        return true;
      },
    },
    {
      key: 'close',
      icon: 'task_alt',
      label: 'maintenance.actions.close',
      primary: true,
      permissions: [P.maintenanceManage],
      visible: isOpen,
      run: async (ctx, r) => {
        const o = r as unknown as MaintenanceOrderDetail;
        const result = await ctx.form(
          {
            title: ctx.i18n.t('maintenance.closeTitle', { folio: o.summary.folio }),
            fields: [
              { key: 'closedAt', label: 'maintenance.closedAt', type: 'datetime', required: true },
              { key: 'odometer', label: 'fields.odometer', type: 'number', min: 0 },
              { key: 'hours', label: 'fields.hourMeter', type: 'number', min: 0 },
              { key: 'workPerformed', label: 'fields.workPerformed', type: 'textarea', required: true },
              { key: 'releaseUnit', label: 'maintenance.releaseUnit', type: 'checkbox', span: 'full' },
            ],
            save: (api, v) => api.post(`maintenance/orders/${o.summary.id}/close`, { ...v, closedAt: new Date(v['closedAt'] as string).toISOString() }),
            width: '640px',
          },
          { closedAt: nowLocalInput(), releaseUnit: true, workPerformed: o.workPerformed },
        );
        return !!result;
      },
    },
    {
      key: 'cancel',
      icon: 'cancel',
      label: 'common.cancel',
      permissions: [P.maintenanceManage],
      visible: isOpen,
      run: async (ctx, r) => {
        const reason = await ctx.dialogs.prompt(ctx.i18n.t('maintenance.cancelTitle'), ctx.i18n.t('fields.reason'));
        if (!reason) {
          return false;
        }

        await firstValueFrom(ctx.api.post(`maintenance/orders/${r['id'] as string}/cancel`, { reason }));
        return true;
      },
    },
  ],
};

// ---- Fuel --------------------------------------------------------------------------------------------------

const fuelFields: FieldDef[] = [
  { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units', required: true },
  { key: 'loadedAt', label: 'fuel.loadedAt', type: 'datetime', required: true },
  { key: 'liters', label: 'fuel.liters', type: 'number', required: true, min: 0.1, max: 5000 },
  { key: 'pricePerLiter', label: 'fuel.pricePerLiter', type: 'money', required: true },
  { key: 'odometer', label: 'fields.odometer', type: 'number', min: 0 },
  { key: 'hours', label: 'fields.hourMeter', type: 'number', min: 0 },
  { key: 'isFullTank', label: 'fuel.fullTank', type: 'checkbox' },
  { key: 'paymentMethod', label: 'fuel.paymentMethod', type: 'enum', enum: 'fuelPaymentMethod', values: ['cash', 'card', 'voucher', 'credit'], required: true },
  { key: 'station', label: 'fuel.station', type: 'text' },
  { key: 'driverId', label: 'fuel.driver', type: 'lookup', catalog: 'personnel/employees' },
  { key: 'receiptFileId', label: 'fuel.receipt', type: 'file', category: 'receipt', fileUrlKey: 'receiptUrl' },
  { key: 'notes', label: 'fields.notes', type: 'textarea' },
];

export const FUEL_DIALOG: DialogSpec = {
  titleKey: 'records.fuelLoad',
  fields: fuelFields,
  defaults: () => ({ loadedAt: nowLocalInput(), isFullTank: true, paymentMethod: 'card' }),
  save: (api, v) => api.post('fuel/loads', { ...v, loadedAt: new Date(v['loadedAt'] as string).toISOString(), workOrderId: null }),
};

export const FUEL_LIST: ListConfig<FuelLoad> = {
  key: 'fuelLoads',
  icon: 'local_gas_station',
  api: 'fuel/loads',
  exportDataset: 'fuel',
  create: { permissions: [P.fuelCapture, P.fuelManage], dialog: FUEL_DIALOG, label: 'fuel.new' },
  headerActions: [{ key: 'efficiency', icon: 'speed', label: 'nav.fuelEfficiency', path: 'combustible/rendimiento', permissions: [P.fuelRead, P.fuelManage] }],
  filters: [
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
    { key: 'onlyAnomalies', label: 'fuel.onlyAnomalies', type: 'toggle' },
  ],
  columns: [
    { key: 'loadedAt', label: 'fuel.loadedAt', type: 'datetime', sub: (r) => r.station },
    { key: 'unitCode', label: 'fields.unit', sub: (r) => r.driverName },
    { key: 'liters', label: 'fuel.liters', type: 'number', align: 'end' },
    { key: 'amount', label: 'fields.amount', type: 'money', align: 'end', wide: true },
    { key: 'odometer', label: 'fields.meters', align: 'end', value: (r) => r.odometer ?? r.hours, wide: true },
    { key: 'efficiency', label: 'fuel.efficiency', align: 'end', value: (r) => (r.efficiency === null ? '—' : `${r.efficiency} ${r.efficiencyUnit}`), sub: (r) => (r.deviationPercent === null ? null : `${r.deviationPercent > 0 ? '+' : ''}${r.deviationPercent} %`) },
    { key: 'isAnomaly', label: 'fuel.anomaly', value: (r) => (r.isAnomaly ? '⚠' : ''), align: 'end' },
  ],
  rowActions: [
    {
      key: 'edit',
      icon: 'edit',
      label: 'common.edit',
      permissions: [P.fuelManage],
      run: async (ctx, row) =>
        !!(await ctx.form(
          { ...FUEL_DIALOG, save: (api, v) => api.put(`fuel/loads/${row.id}`, { ...v, loadedAt: new Date(v['loadedAt'] as string).toISOString(), workOrderId: row.workOrderId }) },
          { ...row, loadedAt: toLocalInput(row.loadedAt) },
        )),
    },
    {
      key: 'delete',
      icon: 'delete',
      label: 'common.delete',
      permissions: [P.fuelManage],
      run: async (ctx, row) => {
        if (!(await ctx.dialogs.confirm(ctx.i18n.t('common.deleteTitle'), ctx.i18n.t('fuel.deleteMessage'), ctx.i18n.t('common.delete'), true))) {
          return false;
        }

        await firstValueFrom(ctx.api.delete(`fuel/loads/${row.id}`));
        return true;
      },
    },
  ],
};

export const FUEL_EFFICIENCY_LIST: ListConfig<FuelEfficiencyRow> = {
  key: 'fuelEfficiency',
  icon: 'speed',
  api: 'fuel/efficiency',
  paged: false,
  link: (r) => ['flota/unidades', r.unitId],
  filters: [
    { key: 'from', label: 'fields.from', type: 'date', default: firstOfMonth(-2) },
    { key: 'to', label: 'fields.to', type: 'date', default: today() },
  ],
  columns: [
    { key: 'unitCode', label: 'fields.unit', sub: (r) => r.unitName },
    { key: 'liters', label: 'fuel.liters', type: 'number', align: 'end' },
    { key: 'amount', label: 'fields.amount', type: 'money', align: 'end', wide: true },
    { key: 'distance', label: 'fuel.distance', align: 'end', value: (r) => r.distance ?? r.hoursWorked, wide: true },
    { key: 'efficiency', label: 'fuel.efficiency', align: 'end', value: (r) => (r.efficiency === null ? '—' : `${r.efficiency} ${r.efficiencyUnit}`), sub: (r) => (r.expected === null ? null : `esperado ${r.expected}`) },
    { key: 'deviationPercent', label: 'fuel.deviation', align: 'end', value: (r) => (r.deviationPercent === null ? '—' : `${r.deviationPercent} %`) },
    { key: 'anomalies', label: 'fuel.anomalies', type: 'number', align: 'end', sub: (r) => `${r.loads} cargas` },
    { key: 'costPerKm', label: 'fuel.costPerKm', type: 'money', align: 'end', wide: true },
  ],
};
