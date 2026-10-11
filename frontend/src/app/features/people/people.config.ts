import { firstValueFrom } from 'rxjs';
import { addDays, firstOfMonth, nowLocalInput, toLocalInput, today } from '../../core/format';
import { ExpenseReport, IncidentDetail, IncidentSummary, InspectionSummary, PayrollRow, SafetyTalk } from '../../core/models';
import { P } from '../../core/permissions';
import { ActionContext, DialogSpec, ListConfig } from '../../shared/crud/list-page';
import { RecordConfig } from '../../shared/crud/record-page';

// ---- Payroll input ----------------------------------------------------------------------------------------

export const PAYROLL_LIST: ListConfig<PayrollRow> = {
  key: 'payroll',
  icon: 'payments',
  api: 'attendance/payroll',
  paged: false,
  exportDataset: 'attendance',
  link: (r) => ['personal/empleados', r.employeeId],
  headerActions: [{ key: 'attendance', icon: 'how_to_reg', label: 'nav.attendance', path: 'cuadrillas/asistencia', permissions: [P.crewsAttendance, P.crewsManage] }],
  filters: [
    { key: 'from', label: 'fields.from', type: 'date', default: addDays(today(), -14) },
    { key: 'to', label: 'fields.to', type: 'date', default: today() },
    { key: 'crewId', label: 'fields.crew', type: 'lookup', catalog: 'crews' },
  ],
  columns: [
    { key: 'name', label: 'fields.employee', sub: (r) => `${r.code}${r.position ? ' · ' + r.position : ''}` },
    { key: 'daysWorked', label: 'payroll.daysWorked', type: 'number', align: 'end' },
    { key: 'hours', label: 'payroll.hours', type: 'number', align: 'end' },
    { key: 'overtimeHours', label: 'payroll.overtime', type: 'number', align: 'end' },
    { key: 'absences', label: 'payroll.absences', type: 'number', align: 'end', wide: true },
    { key: 'rest', label: 'payroll.rest', type: 'number', align: 'end', wide: true },
    { key: 'leave', label: 'payroll.leave', type: 'number', align: 'end', wide: true },
    { key: 'cost', label: 'payroll.cost', type: 'money', align: 'end' },
  ],
};

// ---- Expenses ----------------------------------------------------------------------------------------------

const EXPENSE_STATUSES = ['draft', 'submitted', 'approved', 'rejected', 'paid'] as const;

export const EXPENSE_LIST: ListConfig<ExpenseReport> = {
  key: 'expenses',
  icon: 'receipt',
  api: 'expenses',
  link: (r) => ['viaticos', r.id],
  exportDataset: 'expenses',
  create: { permissions: [P.expensesCapture, P.expensesApprove], path: 'viaticos/nuevo', label: 'expenses.new' },
  filters: [
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'expenseStatus', values: EXPENSE_STATUSES },
    { key: 'employeeId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.purpose },
    { key: 'employeeName', label: 'fields.employee', sub: (r) => r.workOrderFolio },
    { key: 'periodStart', label: 'fields.period', value: (r) => `${r.periodStart} → ${r.periodEnd}`, wide: true },
    { key: 'advanceAmount', label: 'expenses.advance', type: 'money', align: 'end', wide: true },
    { key: 'total', label: 'expenses.proven', type: 'money', align: 'end' },
    { key: 'balance', label: 'fields.balance', type: 'money', align: 'end', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'expenseStatus' },
  ],
};

const expenseEditable = (r: Record<string, unknown>) => r['status'] === 'draft' || r['status'] === 'rejected';

export const EXPENSE_RECORD: RecordConfig = {
  key: 'expense',
  icon: 'receipt',
  api: 'expenses',
  listPath: 'viaticos',
  manage: [P.expensesCapture, P.expensesApprove],
  editable: expenseEditable,
  fields: [
    { key: 'employeeId', label: 'fields.employee', type: 'lookup', catalog: 'personnel/employees', required: true },
    { key: 'purpose', label: 'expenses.purpose', type: 'text', required: true, maxLength: 300 },
    { key: 'periodStart', label: 'fields.periodStart', type: 'date', required: true },
    { key: 'periodEnd', label: 'fields.periodEnd', type: 'date', required: true },
    { key: 'advanceAmount', label: 'expenses.advance', type: 'money', required: true, min: 0 },
    {
      key: 'items',
      label: 'expenses.items',
      type: 'array',
      addLabel: 'expenses.addItem',
      fields: [
        { key: 'date', label: 'fields.date', type: 'date', required: true },
        { key: 'category', label: 'fields.category', type: 'enum', enum: 'expenseCategory', values: ['meals', 'lodging', 'tolls', 'fuel', 'transport', 'materials', 'other'], required: true },
        { key: 'description', label: 'fields.description', type: 'text', required: true },
        { key: 'vendor', label: 'expenses.vendor', type: 'text' },
        { key: 'amount', label: 'fields.amount', type: 'money', required: true, min: 0 },
        { key: 'hasInvoice', label: 'expenses.hasInvoice', type: 'checkbox' },
        { key: 'invoiceUuid', label: 'fields.uuid', type: 'text', upper: true, showIf: (v) => v['hasInvoice'] === true },
        { key: 'receiptFileId', label: 'expenses.receipt', type: 'file', category: 'receipt', fileUrlKey: 'receiptUrl' },
      ],
    },
  ],
  defaults: (_query, ctx) => ({
    employeeId: ctx.session.tenant()?.membership.employeeId ?? null,
    periodStart: addDays(today(), -6),
    periodEnd: today(),
    advanceAmount: 0,
    items: [],
  }),
  toForm: (r) => {
    const e = r as unknown as ExpenseReport;
    return { ...e, items: e.items.map((i) => ({ ...i })) };
  },
  toPayload: (v) => ({ ...v, workOrderId: null }),
  title: (r) => r['folio'] as string,
  subtitle: (r) => `${(r['employeeName'] as string) ?? ''} · ${r['purpose'] as string}`,
  status: { enum: 'expenseStatus', value: (r) => r['status'] as string },
  warnings: (r) => (r['rejectionReason'] ? [`Rechazado: ${r['rejectionReason'] as string}`] : []),
  actions: [
    { key: 'submit', icon: 'send', label: 'expenses.actions.submit', primary: true, permissions: [P.expensesCapture, P.expensesApprove], visible: expenseEditable, run: (ctx, r) => expenseAction(ctx, r, 'submit') },
    { key: 'approve', icon: 'thumb_up', label: 'expenses.actions.approve', primary: true, permissions: [P.expensesApprove], visible: (r) => r['status'] === 'submitted', run: (ctx, r) => expenseAction(ctx, r, 'approve') },
    { key: 'reject', icon: 'thumb_down', label: 'expenses.actions.reject', permissions: [P.expensesApprove], visible: (r) => r['status'] === 'submitted', run: (ctx, r) => expenseAction(ctx, r, 'reject') },
    { key: 'paid', icon: 'paid', label: 'expenses.actions.markPaid', permissions: [P.expensesApprove], visible: (r) => r['status'] === 'approved', run: (ctx, r) => expenseAction(ctx, r, 'markPaid') },
  ],
};

async function expenseAction(ctx: ActionContext, r: Record<string, unknown>, action: string): Promise<boolean> {
  let reason: string | null = null;
  if (action === 'reject') {
    reason = await ctx.dialogs.prompt(ctx.i18n.t('expenses.rejectTitle'), ctx.i18n.t('fields.reason'));
    if (!reason) {
      return false;
    }
  }

  await firstValueFrom(ctx.api.post(`expenses/${r['id'] as string}/status`, { action, reason }));
  return true;
}

// ---- HSE ----------------------------------------------------------------------------------------------------

const INCIDENT_TYPES = ['incident', 'accident', 'nearMiss', 'unsafeCondition', 'unsafeAct', 'spill', 'equipmentDamage', 'environmental', 'vehicleAccident'] as const;
const SEVERITIES = ['low', 'medium', 'high', 'critical'] as const;

export const INCIDENT_LIST: ListConfig<IncidentSummary> = {
  key: 'incidents',
  icon: 'report',
  api: 'hse/incidents',
  link: (r) => ['hse/incidentes', r.id],
  exportDataset: 'incidents',
  create: { permissions: [P.hseReport, P.hseManage], path: 'hse/incidentes/nuevo', label: 'hse.reportIncident' },
  headerActions: [{ key: 'stats', icon: 'monitor_heart', label: 'nav.hseStats', path: 'hse/indicadores', permissions: [P.hseRead, P.hseManage] }],
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'incidentStatus', values: ['reported', 'investigating', 'closed'] },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'incidentType', values: INCIDENT_TYPES },
    { key: 'severity', label: 'hse.severity', type: 'enum', enum: 'incidentSeverity', values: SEVERITIES },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.description },
    { key: 'occurredAt', label: 'hse.occurredAt', type: 'datetime' },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'incidentType' },
    { key: 'severity', label: 'hse.severity', type: 'status', enum: 'incidentSeverity' },
    { key: 'siteName', label: 'fields.site', sub: (r) => r.unitCode, wide: true },
    { key: 'openActions', label: 'hse.openActions', type: 'number', align: 'end', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'incidentStatus' },
  ],
};

export const INCIDENT_RECORD: RecordConfig = {
  key: 'incident',
  icon: 'report',
  api: 'hse/incidents',
  listPath: 'hse/incidentes',
  manage: [P.hseReport, P.hseManage],
  editable: (r) => (r['summary'] as IncidentSummary).status !== 'closed',
  fields: [
    { key: 'occurredAt', label: 'hse.occurredAt', type: 'datetime', required: true },
    { key: 'type', label: 'fields.type', type: 'enum', enum: 'incidentType', values: INCIDENT_TYPES, required: true },
    { key: 'severity', label: 'hse.severity', type: 'enum', enum: 'incidentSeverity', values: SEVERITIES, required: true },
    { key: 'siteId', label: 'fields.site', type: 'lookup', catalog: 'catalogs/sites' },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
    { key: 'location', label: 'hse.location', type: 'text' },
    { key: 'employeeIds', label: 'hse.involved', type: 'lookups', catalog: 'personnel/employees', span: 'full' },
    { key: 'description', label: 'hse.whatHappened', type: 'textarea', required: true },
    { key: 'immediateActions', label: 'hse.immediateActions', type: 'textarea' },
    { key: 'rootCause', label: 'hse.rootCause', type: 'textarea' },
    { key: 'lostDays', label: 'hse.lostDays', type: 'number', min: 0 },
    {
      key: 'actions',
      label: 'hse.correctiveActions',
      type: 'array',
      addLabel: 'hse.addAction',
      fields: [
        { key: 'description', label: 'fields.description', type: 'text', required: true, span: 'full' },
        { key: 'responsible', label: 'fields.responsible', type: 'text' },
        { key: 'dueOn', label: 'fields.dueOn', type: 'date' },
        { key: 'status', label: 'fields.status', type: 'enum', enum: 'actionStatus', values: ['pending', 'inProgress', 'done'], required: true },
        { key: 'completedOn', label: 'hse.completedOn', type: 'date', showIf: (v) => v['status'] === 'done' },
      ],
    },
  ],
  defaults: () => ({ occurredAt: nowLocalInput(), type: 'nearMiss', severity: 'low', employeeIds: [], actions: [] }),
  toForm: (r) => {
    const i = r as unknown as IncidentDetail;
    return {
      occurredAt: toLocalInput(i.summary.occurredAt),
      type: i.summary.type,
      severity: i.summary.severity,
      siteId: i.siteId,
      unitId: i.unitId,
      location: i.location,
      employeeIds: i.employees.map((e) => e.id),
      description: i.summary.description,
      immediateActions: i.immediateActions,
      rootCause: i.rootCause,
      lostDays: i.lostDays,
      actions: i.actions,
      photoFileIds: i.photos.map((p) => p.fileId),
    };
  },
  toPayload: (v, record) => ({
    ...v,
    occurredAt: new Date(v['occurredAt'] as string).toISOString(),
    workOrderId: (record?.['workOrderId'] as string) ?? null,
    photoFileIds: ((record as unknown as IncidentDetail | null)?.photos ?? []).map((p) => p.fileId),
  }),
  title: (r) => (r['summary'] as IncidentSummary).folio,
  subtitle: (r) => (r['summary'] as IncidentSummary).reportedByName,
  status: { enum: 'incidentStatus', value: (r) => (r['summary'] as IncidentSummary).status },
  actions: [
    {
      key: 'close',
      icon: 'task_alt',
      label: 'hse.close',
      primary: true,
      permissions: [P.hseManage],
      visible: (r) => (r['summary'] as IncidentSummary).status !== 'closed',
      run: async (ctx, r) => {
        await firstValueFrom(ctx.api.post(`hse/incidents/${r['id'] as string}/close`));
        return true;
      },
    },
    {
      key: 'reopen',
      icon: 'lock_open',
      label: 'hse.reopen',
      permissions: [P.hseManage],
      visible: (r) => (r['summary'] as IncidentSummary).status === 'closed',
      run: async (ctx, r) => {
        await firstValueFrom(ctx.api.post(`hse/incidents/${r['id'] as string}/close?reopen=true`));
        return true;
      },
    },
  ],
};

export const INSPECTION_LIST: ListConfig<InspectionSummary> = {
  key: 'inspections',
  icon: 'fact_check',
  api: 'hse/inspections',
  link: (r) => ['hse/inspecciones', r.id],
  create: { permissions: [P.hseReport, P.hseManage], path: 'hse/inspecciones/nueva', label: 'hse.newInspection' },
  headerActions: [{ key: 'templates', icon: 'rule', label: 'nav.inspectionTemplates', path: 'hse/plantillas', permissions: [P.hseManage] }],
  filters: [
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'inspectionCategory', values: ['unitPreUse', 'ppe', 'site', 'audit', 'other'] },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
    { key: 'onlyFailed', label: 'hse.onlyFailed', type: 'toggle' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'templateName', label: 'hse.template', sub: (r) => r.inspectorName },
    { key: 'performedAt', label: 'hse.performedAt', type: 'datetime' },
    { key: 'category', label: 'fields.category', type: 'enum', enum: 'inspectionCategory', wide: true },
    { key: 'unitCode', label: 'fields.unit', value: (r) => r.unitCode ?? r.siteName ?? '—' },
    { key: 'score', label: 'hse.score', align: 'end', value: (r) => `${Math.round(r.score)} %`, sub: (r, t) => (r.findings ? t('hse.findingsCount', { n: r.findings }) : null) },
    { key: 'passed', label: 'hse.result', value: (r, t) => (r.passed ? `✔ ${t('hse.passed')}` : `✖ ${t('hse.notPassed')}`) },
  ],
};

export const TALK_DIALOG: DialogSpec = {
  titleKey: 'records.safetyTalk',
  width: '760px',
  fields: [
    { key: 'heldAt', label: 'hse.heldAt', type: 'datetime', required: true },
    { key: 'topic', label: 'hse.topic', type: 'text', required: true, span: 'full' },
    { key: 'durationMinutes', label: 'hse.duration', type: 'number', min: 1, required: true },
    { key: 'instructor', label: 'hse.instructor', type: 'text', required: true },
    { key: 'siteId', label: 'fields.site', type: 'lookup', catalog: 'catalogs/sites' },
    { key: 'attendeeIds', label: 'hse.attendees', type: 'lookups', catalog: 'personnel/employees', span: 'full' },
    { key: 'externalAttendees', label: 'hse.externalAttendees', type: 'textarea', hint: 'hse.externalHint' },
    { key: 'notes', label: 'fields.notes', type: 'textarea' },
  ],
  defaults: () => ({ heldAt: nowLocalInput(), durationMinutes: 15, attendeeIds: [] }),
  save: (api, v) => api.post('hse/talks', { ...v, heldAt: new Date(v['heldAt'] as string).toISOString(), workOrderId: null, evidenceFileIds: [] }),
};

export const TALK_LIST: ListConfig<SafetyTalk> = {
  key: 'safetyTalks',
  icon: 'record_voice_over',
  api: 'hse/talks',
  create: { permissions: [P.hseReport, P.hseManage], dialog: TALK_DIALOG, label: 'hse.newTalk' },
  filters: [
    { key: 'from', label: 'fields.from', type: 'date', default: firstOfMonth(-2) },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'heldAt', label: 'hse.heldAt', type: 'datetime' },
    { key: 'topic', label: 'hse.topic', sub: (r) => r.instructor },
    { key: 'siteName', label: 'fields.site', wide: true },
    { key: 'durationMinutes', label: 'hse.duration', type: 'number', align: 'end', wide: true },
    { key: 'attendees', label: 'hse.attendees', align: 'end', value: (r) => r.attendees.length },
  ],
  rowActions: [
    {
      key: 'edit',
      icon: 'edit',
      label: 'common.edit',
      permissions: [P.hseReport, P.hseManage],
      run: async (ctx, row) =>
        !!(await ctx.form(
          {
            ...TALK_DIALOG,
            save: (api, v) => api.put(`hse/talks/${row.id}`, { ...v, heldAt: new Date(v['heldAt'] as string).toISOString(), workOrderId: row.workOrderId, evidenceFileIds: row.evidenceFileIds }),
          },
          { ...row, heldAt: toLocalInput(row.heldAt), attendeeIds: row.attendees.map((a) => a.id) },
        )),
    },
  ],
};
