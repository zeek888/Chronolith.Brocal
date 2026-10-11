import { firstValueFrom } from 'rxjs';
import { saveFile } from '../../core/api';
import { firstOfMonth, nowLocalInput, toLocalInput } from '../../core/format';
import { CollectionNote, Estimate, InvoiceSummary, PaymentSummary, TripDetail, TripSummary } from '../../core/models';
import { P } from '../../core/permissions';
import { ActionContext, ListConfig } from '../../shared/crud/list-page';
import { RecordConfig } from '../../shared/crud/record-page';
import { registerPayment } from './register-payment';

const INVOICE_STATUSES = ['draft', 'stamped', 'cancellationPending', 'cancelled'] as const;

export const INVOICE_LIST: ListConfig<InvoiceSummary> = {
  key: 'invoices',
  icon: 'request_page',
  api: 'invoices',
  link: (r) => ['facturacion/facturas', r.id],
  exportDataset: 'invoices',
  create: { permissions: [P.invoicingIssue], path: 'facturacion/nueva', label: 'invoices.fromTickets' },
  filters: [
    { key: 'search', label: 'invoices.searchHint', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'invoiceStatus', values: INVOICE_STATUSES },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
    { key: 'onlyWithBalance', label: 'invoices.withBalance', type: 'toggle' },
    { key: 'onlyOverdue', label: 'invoices.overdueOnly', type: 'toggle' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', value: (r) => (r.folio ? `${r.series}-${r.folio}` : `${r.series} · borrador`), sub: (r) => r.uuid },
    { key: 'issueDate', label: 'fields.issueDate', type: 'date' },
    { key: 'receiverName', label: 'fields.customer', sub: (r) => r.receiverRfc },
    { key: 'copadeNumber', label: 'fields.copade', wide: true },
    { key: 'dueOn', label: 'fields.dueOn', type: 'date', wide: true, sub: (r) => (r.isOverdue ? `${r.daysOverdue} d vencida` : null) },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'invoiceStatus' },
    { key: 'total', label: 'fields.total', type: 'money', align: 'end', currency: (r) => r.currency },
    { key: 'balance', label: 'fields.balance', type: 'money', align: 'end', currency: (r) => r.currency },
  ],
  muted: (r) => r.status === 'cancelled',
};

// ---- Collections -----------------------------------------------------------------------------------------

export const PAYMENT_LIST: ListConfig<PaymentSummary> = {
  key: 'payments',
  icon: 'paid',
  api: 'collections/payments',
  exportDataset: 'payments',
  headerActions: [
    {
      key: 'register',
      icon: 'add',
      label: 'payments.register',
      permissions: [P.collectionsManage],
      run: (ctx, filters) => registerPayment(ctx, (filters['customerId'] as string | null) ?? null),
    },
  ],
  filters: [
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'paidAt', label: 'fields.paidAt', type: 'datetime' },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r.reference },
    { key: 'paymentForm', label: 'fields.paymentForm', wide: true },
    { key: 'invoices', label: 'fields.invoices', type: 'number', align: 'end', wide: true },
    { key: 'complementStatus', label: 'fields.complement', type: 'status', enum: 'paymentComplementStatus', sub: (r) => r.complementUuid },
    { key: 'amount', label: 'fields.amount', type: 'money', align: 'end' },
  ],
  rowActions: [
    {
      key: 'complement',
      icon: 'receipt',
      label: 'payments.stampComplement',
      permissions: [P.collectionsManage, P.invoicingIssue],
      visible: (r) => r.complementStatus === 'pending',
      run: async (ctx, row) => {
        const uuid = await firstValueFrom(ctx.api.post<string>(`collections/payments/${row.id}/complement`));
        ctx.dialogs.success(ctx.i18n.t('payments.complementStamped', { uuid }));
        return true;
      },
    },
    {
      key: 'delete',
      icon: 'delete',
      label: 'common.delete',
      permissions: [P.collectionsManage],
      visible: (r) => r.complementStatus !== 'stamped',
      run: async (ctx, row) => {
        if (!(await ctx.dialogs.confirm(ctx.i18n.t('payments.deleteTitle'), ctx.i18n.t('payments.deleteMessage'), ctx.i18n.t('common.delete'), true))) {
          return false;
        }

        await firstValueFrom(ctx.api.delete(`collections/payments/${row.id}`));
        return true;
      },
    },
  ],
};

const ESTIMATE_STATUSES = ['draft', 'submitted', 'approved', 'copadeAssigned', 'invoiced', 'paid', 'rejected'] as const;

const estimateFields = [
  { key: 'customerId', label: 'fields.customer', type: 'lookup' as const, catalog: 'catalogs/customers', required: true },
  { key: 'contractId', label: 'fields.contract', type: 'lookup' as const, catalog: 'catalogs/contracts', parent: 'customerId' },
  { key: 'periodStart', label: 'fields.periodStart', type: 'date' as const, required: true },
  { key: 'periodEnd', label: 'fields.periodEnd', type: 'date' as const, required: true },
  { key: 'amount', label: 'fields.amount', type: 'money' as const, required: true, min: 0, hint: 'estimates.amountHint' },
  { key: 'description', label: 'fields.description', type: 'textarea' as const, required: true },
  { key: 'notes', label: 'fields.notes', type: 'textarea' as const },
];

export const ESTIMATE_LIST: ListConfig<Estimate> = {
  key: 'estimates',
  icon: 'assignment_turned_in',
  api: 'collections/estimates',
  filters: [
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'estimateStatus', values: ESTIMATE_STATUSES },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
  ],
  create: {
    permissions: [P.collectionsManage],
    label: 'estimates.new',
    dialog: {
      titleKey: 'estimates.new',
      fields: estimateFields,
      defaults: () => ({ periodStart: firstOfMonth(-1), periodEnd: firstOfMonth(0), amount: null }),
      save: (api, value) => api.post('collections/estimates', { ...value, ticketIds: [] }),
    },
  },
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.description },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r.contractName },
    { key: 'periodStart', label: 'fields.period', value: (r) => `${r.periodStart} → ${r.periodEnd}`, wide: true },
    { key: 'copadeNumber', label: 'fields.copade', sub: (r) => r.invoiceReference },
    { key: 'daysInStatus', label: 'estimates.daysInStatus', type: 'number', align: 'end', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'estimateStatus' },
    { key: 'amount', label: 'fields.amount', type: 'money', align: 'end' },
  ],
  rowActions: [
    {
      key: 'edit',
      icon: 'edit',
      label: 'common.edit',
      permissions: [P.collectionsManage],
      visible: (r) => r.status === 'draft' || r.status === 'rejected',
      run: async (ctx, row) =>
        !!(await ctx.form(
          { title: row.folio, fields: estimateFields, save: (api, value) => api.put(`collections/estimates/${row.id}`, { ...value, ticketIds: row.ticketIds }) },
          { ...row },
        )),
    },
    { key: 'submit', icon: 'send', label: 'estimates.actions.submit', permissions: [P.collectionsManage], visible: (r) => r.status === 'draft' || r.status === 'rejected', run: (ctx, row) => estimateAction(ctx, row, 'submit') },
    { key: 'approve', icon: 'thumb_up', label: 'estimates.actions.approve', permissions: [P.collectionsManage], visible: (r) => r.status === 'submitted', run: (ctx, row) => estimateAction(ctx, row, 'approve') },
    { key: 'reject', icon: 'thumb_down', label: 'estimates.actions.reject', permissions: [P.collectionsManage], visible: (r) => r.status === 'submitted', run: (ctx, row) => estimateAction(ctx, row, 'reject') },
    { key: 'copade', icon: 'confirmation_number', label: 'estimates.actions.assignCopade', permissions: [P.collectionsManage], visible: (r) => r.status === 'approved' || r.status === 'copadeAssigned', run: (ctx, row) => estimateAction(ctx, row, 'assignCopade') },
    {
      key: 'invoice',
      icon: 'request_page',
      label: 'estimates.actions.invoice',
      permissions: [P.invoicingIssue],
      visible: (r) => (r.status === 'approved' || r.status === 'copadeAssigned') && !r.invoiceId,
      run: async (ctx, row) => {
        const stamp = await ctx.dialogs.confirm(ctx.i18n.t('estimates.invoiceTitle'), ctx.i18n.t('estimates.invoiceMessage'), ctx.i18n.t('invoices.stampNow'));
        const id = await firstValueFrom(ctx.api.post<string>(`collections/estimates/${row.id}/invoice?stamp=${stamp}`));
        await ctx.nav.navigate(['facturacion/facturas', id]);
        return false;
      },
    },
  ],
};

async function estimateAction(ctx: ActionContext, row: Estimate, action: string): Promise<boolean> {
  let reason: string | null = null;
  let copade: string | null = null;
  if (action === 'reject') {
    reason = await ctx.dialogs.prompt(ctx.i18n.t('estimates.rejectTitle'), ctx.i18n.t('fields.reason'));
    if (!reason) {
      return false;
    }
  }

  if (action === 'assignCopade' || action === 'approve') {
    copade = await ctx.dialogs.prompt(ctx.i18n.t('estimates.copadeTitle'), ctx.i18n.t('fields.copade'), {
      required: action === 'assignCopade',
      multiline: false,
      value: row.copadeNumber ?? '',
      message: ctx.i18n.t(action === 'approve' ? 'estimates.copadeOptional' : 'estimates.copadeMessage'),
    });
    if (action === 'assignCopade' && !copade) {
      return false;
    }
  }

  await firstValueFrom(ctx.api.post(`collections/estimates/${row.id}/status`, { action, reason, copadeNumber: copade }));
  return true;
}

const CHANNELS = ['phone', 'email', 'whatsApp', 'visit', 'portal'] as const;

export const COLLECTION_NOTE_LIST: ListConfig<CollectionNote> = {
  key: 'collectionNotes',
  icon: 'support_agent',
  api: 'collections/notes',
  filters: [{ key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' }],
  create: {
    permissions: [P.collectionsManage],
    label: 'collectionNotes.new',
    dialog: {
      titleKey: 'records.collectionNote',
      fields: [
        { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', required: true },
        { key: 'channel', label: 'fields.channel', type: 'enum', enum: 'contactChannel', values: CHANNELS, required: true },
        { key: 'contactedAt', label: 'fields.contactedAt', type: 'datetime', required: true },
        { key: 'contactName', label: 'fields.contactName', type: 'text' },
        { key: 'promisedPaymentOn', label: 'fields.promisedPaymentOn', type: 'date' },
        { key: 'nextActionOn', label: 'fields.nextActionOn', type: 'date', hint: 'collectionNotes.nextActionHint' },
        { key: 'comment', label: 'fields.comment', type: 'textarea', required: true },
      ],
      defaults: () => ({ channel: 'phone', contactedAt: nowLocalInput() }),
      save: (api, value) => api.post('collections/notes', { ...value, contactedAt: new Date(value['contactedAt'] as string).toISOString(), invoiceId: null, estimateId: null }),
    },
  },
  columns: [
    { key: 'contactedAt', label: 'fields.contactedAt', type: 'datetime' },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r.invoiceReference },
    { key: 'channel', label: 'fields.channel', type: 'enum', enum: 'contactChannel', wide: true },
    { key: 'comment', label: 'fields.comment', sub: (r) => r.contactName },
    { key: 'promisedPaymentOn', label: 'fields.promisedPaymentOn', type: 'date' },
    { key: 'nextActionOn', label: 'fields.nextActionOn', type: 'date', wide: true },
    { key: 'createdBy', label: 'fields.createdBy', wide: true },
  ],
};

// ---- Carta Porte ------------------------------------------------------------------------------------------

export const TRIP_LIST: ListConfig<TripSummary> = {
  key: 'cartaPorte',
  icon: 'route',
  api: 'cartaporte/trips',
  link: (r) => ['cartaporte', r.id],
  create: { permissions: [P.cartaPorteManage], path: 'cartaporte/nuevo', label: 'trips.new' },
  filters: [
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'tripStatus', values: ['draft', 'stamped', 'cancelled'] },
    { key: 'unitId', label: 'fields.unit', type: 'lookup', catalog: 'fleet/units' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.uuid },
    { key: 'departure', label: 'trips.departure', type: 'datetime' },
    { key: 'originName', label: 'trips.route', value: (r) => `${r.originName} → ${r.destinationName}`, sub: (r) => `${r.distanceKm} km` },
    { key: 'unitCode', label: 'fields.unit', sub: (r) => r.operatorName },
    { key: 'hasHazardousGoods', label: 'trips.hazardous', type: 'bool', wide: true },
    { key: 'cfdiType', label: 'trips.cfdiType', type: 'enum', enum: 'cfdiType', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'tripStatus' },
  ],
  muted: (r) => r.status === 'cancelled',
};

const location = (prefix: 'origin' | 'destination') => [
  { key: `${prefix}Section`, label: `trips.${prefix}`, type: 'section' as const },
  { key: `${prefix}SiteId`, label: 'fields.site', type: 'lookup' as const, catalog: 'catalogs/sites', hint: 'trips.siteHint' },
  { key: `${prefix}Name`, label: 'fields.name', type: 'text' as const, required: true },
  { key: `${prefix}Rfc`, label: 'fields.rfc', type: 'text' as const, upper: true },
  { key: `${prefix}PostalCode`, label: 'fields.postalCode', type: 'text' as const, required: true, pattern: '^\\d{5}$' },
  { key: `${prefix}State`, label: 'trips.stateCode', type: 'text' as const, required: true, upper: true, hint: 'trips.stateHint' },
  { key: `${prefix}Municipality`, label: 'fields.municipality', type: 'text' as const },
  { key: `${prefix}Address`, label: 'fields.address', type: 'text' as const },
  { key: `${prefix}DateTime`, label: prefix === 'origin' ? 'trips.departure' : 'trips.arrival', type: 'datetime' as const, required: true },
];

/** Viaje de autotanque con Carta Porte 3.1: datos para el CFDI de traslado (o de ingreso por flete). */
export const TRIP_RECORD: RecordConfig = {
  key: 'trip',
  icon: 'route',
  api: 'cartaporte/trips',
  listPath: 'cartaporte',
  manage: [P.cartaPorteManage],
  editable: (r) => (r['summary'] as TripSummary).status === 'draft',
  fields: [
    { key: 'cfdiType', label: 'trips.cfdiType', type: 'enum', enum: 'cfdiType', values: ['transfer', 'income'], required: true, hint: 'trips.cfdiTypeHint' },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'unitId', label: 'trips.tractor', type: 'lookup', catalog: 'fleet/units', required: true },
    { key: 'trailerUnitId', label: 'trips.trailer', type: 'lookup', catalog: 'fleet/units' },
    { key: 'operatorId', label: 'trips.operator', type: 'lookup', catalog: 'personnel/employees', required: true },
    { key: 'distanceKm', label: 'trips.distance', type: 'number', required: true, min: 0.1 },
    ...location('origin'),
    ...location('destination'),
    {
      key: 'goods',
      label: 'trips.goods',
      type: 'array',
      addLabel: 'trips.addGoods',
      fields: [
        { key: 'satGoodsCode', label: 'trips.satGoodsCode', type: 'text', required: true, pattern: '^\\d{8}$' },
        { key: 'description', label: 'fields.description', type: 'text', required: true },
        { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0 },
        { key: 'satUnitCode', label: 'fields.satUnitCode', type: 'text', required: true, upper: true },
        { key: 'weightKg', label: 'trips.weightKg', type: 'number', required: true, min: 0 },
        { key: 'isHazardous', label: 'trips.hazardous', type: 'checkbox' },
        { key: 'hazardousCode', label: 'trips.hazardousCode', type: 'text', showIf: (v) => v['isHazardous'] === true, hint: 'trips.hazardousHint' },
        { key: 'packagingCode', label: 'trips.packagingCode', type: 'text', showIf: (v) => v['isHazardous'] === true },
      ],
    },
    { key: 'freightSection', label: 'trips.freight', type: 'section', showIf: (v) => v['cfdiType'] === 'income' },
    { key: 'freightServiceId', label: 'fields.service', type: 'lookup', catalog: 'pricing/services', showIf: (v) => v['cfdiType'] === 'income' },
    { key: 'freightAmount', label: 'fields.amount', type: 'money', showIf: (v) => v['cfdiType'] === 'income' },
    { key: 'notes', label: 'fields.notes', type: 'textarea' },
  ],
  defaults: () => ({ cfdiType: 'transfer', originDateTime: nowLocalInput(1), destinationDateTime: nowLocalInput(4), goods: [] }),
  toForm: (r) => {
    const trip = r as unknown as TripDetail;
    const flat = (prefix: string, loc: TripDetail['origin']) => ({
      [`${prefix}SiteId`]: loc.siteId,
      [`${prefix}Name`]: loc.name,
      [`${prefix}Rfc`]: loc.rfc,
      [`${prefix}PostalCode`]: loc.postalCode,
      [`${prefix}State`]: loc.state,
      [`${prefix}Municipality`]: loc.municipality,
      [`${prefix}Address`]: loc.address,
      [`${prefix}DateTime`]: toLocalInput(loc.dateTime),
    });
    return {
      cfdiType: trip.summary.cfdiType,
      customerId: trip.customerId,
      unitId: trip.unitId,
      trailerUnitId: trip.trailerUnitId,
      operatorId: trip.operatorId,
      distanceKm: trip.summary.distanceKm,
      ...flat('origin', trip.origin),
      ...flat('destination', trip.destination),
      goods: trip.goods,
      freightServiceId: trip.freightServiceId,
      freightAmount: trip.freightAmount,
      notes: trip.notes,
    };
  },
  toPayload: (v) => {
    const loc = (prefix: string) => ({
      siteId: v[`${prefix}SiteId`] ?? null,
      name: v[`${prefix}Name`],
      rfc: v[`${prefix}Rfc`] ?? null,
      postalCode: v[`${prefix}PostalCode`],
      state: v[`${prefix}State`],
      municipality: v[`${prefix}Municipality`] ?? null,
      address: v[`${prefix}Address`] ?? null,
      dateTime: new Date(v[`${prefix}DateTime`] as string).toISOString(),
    });
    return {
      cfdiType: v['cfdiType'],
      customerId: v['customerId'] ?? null,
      workOrderId: null,
      unitId: v['unitId'],
      trailerUnitId: v['trailerUnitId'] ?? null,
      operatorId: v['operatorId'],
      origin: loc('origin'),
      destination: loc('destination'),
      distanceKm: v['distanceKm'],
      goods: v['goods'],
      freightServiceId: v['cfdiType'] === 'income' ? (v['freightServiceId'] ?? null) : null,
      freightAmount: v['cfdiType'] === 'income' ? (v['freightAmount'] ?? null) : null,
      notes: v['notes'] ?? null,
    };
  },
  title: (r) => (r['summary'] as TripSummary).folio,
  status: { enum: 'tripStatus', value: (r) => (r['summary'] as TripSummary).status },
  subtitle: (r) => {
    const s = r['summary'] as TripSummary;
    return `${s.originName} → ${s.destinationName}${s.uuid ? ' · UUID ' + s.uuid : ''}`;
  },
  warnings: (r) => ((r['summary'] as TripSummary).status === 'draft' ? ((r['missingData'] as string[]) ?? []) : []),
  actions: [
    {
      key: 'stamp',
      icon: 'verified',
      label: 'trips.stamp',
      primary: true,
      permissions: [P.cartaPorteManage],
      visible: (r) => (r['summary'] as TripSummary).status === 'draft',
      run: async (ctx, r) => {
        const uuid = await firstValueFrom(ctx.api.post<string>(`cartaporte/trips/${r['id'] as string}/stamp`));
        ctx.dialogs.success(ctx.i18n.t('trips.stamped', { uuid }));
        return true;
      },
    },
    {
      key: 'invoice',
      icon: 'request_page',
      label: 'trips.viewCfdi',
      visible: (r) => !!r['invoiceId'],
      run: async (_ctx, r) => `facturacion/facturas/${r['invoiceId'] as string}`,
    },
    {
      key: 'xml',
      icon: 'code',
      label: 'common.xml',
      visible: (r) => !!r['invoiceId'],
      run: async (ctx, r) => {
        saveFile(await firstValueFrom(ctx.api.download(`invoices/${r['invoiceId'] as string}/xml`)));
        return false;
      },
    },
    {
      key: 'cancel',
      icon: 'cancel',
      label: 'common.cancel',
      permissions: [P.cartaPorteManage],
      visible: (r) => (r['summary'] as TripSummary).status !== 'cancelled',
      run: async (ctx, r) => {
        const reason = await ctx.dialogs.prompt(ctx.i18n.t('trips.cancelTitle'), ctx.i18n.t('fields.reason'));
        if (!reason) {
          return false;
        }

        await firstValueFrom(ctx.api.post(`cartaporte/trips/${r['id'] as string}/cancel`, { reason }));
        return true;
      },
    },
  ],
};
