import { firstValueFrom } from 'rxjs';
import { openFile } from '../../core/api';
import { addDays, money, nowLocalInput, today } from '../../core/format';
import { QuoteDetail, QuoteSummary, ResolvedPrice } from '../../core/models';
import { P } from '../../core/permissions';
import { ActionContext, ListConfig } from '../../shared/crud/list-page';
import { RecordConfig } from '../../shared/crud/record-page';
import { CURRENCIES } from '../catalogs/catalog-configs';

const STATUSES = ['draft', 'sent', 'accepted', 'rejected', 'expired', 'converted'] as const;

export const QUOTE_LIST: ListConfig<QuoteSummary> = {
  key: 'quotes',
  icon: 'request_quote',
  api: 'quotes',
  link: (r) => ['cotizaciones', r.id],
  create: { permissions: [P.quotesManage], path: 'cotizaciones/nueva', label: 'quotes.new' },
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'quoteStatus', values: STATUSES },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.subject },
    { key: 'customerName', label: 'fields.customer' },
    { key: 'issuedOn', label: 'fields.issuedOn', type: 'date', wide: true },
    { key: 'validUntil', label: 'fields.validUntil', type: 'date', sub: (r) => (r.isExpired ? '⚠' : null) },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'quoteStatus' },
    { key: 'total', label: 'fields.total', type: 'money', align: 'end', currency: (r) => r.currency },
  ],
};

const editable = (r: Record<string, unknown>) => {
  const status = (r['summary'] as QuoteSummary).status;
  return status === 'draft' || status === 'sent';
};

export const QUOTE_RECORD: RecordConfig = {
  key: 'quote',
  icon: 'request_quote',
  api: 'quotes',
  listPath: 'cotizaciones',
  manage: [P.quotesManage],
  editable,
  fields: [
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers', required: true },
    { key: 'contractId', label: 'fields.contract', type: 'lookup', catalog: 'catalogs/contracts', parent: 'customerId' },
    { key: 'siteId', label: 'fields.site', type: 'lookup', catalog: 'catalogs/sites', parent: 'customerId' },
    { key: 'serviceTypeId', label: 'fields.serviceType', type: 'lookup', catalog: 'pricing/service-types' },
    { key: 'issuedOn', label: 'fields.issuedOn', type: 'date', required: true },
    { key: 'validUntil', label: 'fields.validUntil', type: 'date', required: true },
    { key: 'currency', label: 'fields.currency', type: 'select', options: CURRENCIES, required: true },
    { key: 'subject', label: 'fields.subject', type: 'text', span: 'full', maxLength: 300 },
    {
      key: 'lines',
      label: 'quotes.lines',
      type: 'array',
      addLabel: 'fields.addService',
      fields: [
        { key: 'serviceId', label: 'fields.service', type: 'lookup', catalog: 'pricing/services', required: true },
        { key: 'description', label: 'fields.description', type: 'text', hint: 'quotes.descriptionHint' },
        { key: 'quantity', label: 'fields.quantity', type: 'number', required: true, min: 0 },
        { key: 'unitName', label: 'fields.unit', type: 'text' },
        { key: 'unitPrice', label: 'fields.unitPrice', type: 'money', hint: 'quotes.priceHint' },
        { key: 'discountPercent', label: 'fields.discountPercent', type: 'number', min: 0, max: 100 },
        { key: 'vatRate', label: 'fields.vatRate', type: 'percent', min: 0, max: 100 },
      ],
    },
    { key: 'terms', label: 'quotes.terms', type: 'textarea' },
    { key: 'notes', label: 'fields.notes', type: 'textarea' },
  ],
  defaults: (query, ctx) => ({
    customerId: query.get('cliente'),
    issuedOn: today(),
    validUntil: addDays(today(), 30),
    currency: 'MXN',
    terms: ctx.i18n.t('quotes.defaultTerms'),
    lines: [],
  }),
  toForm: (r) => {
    const q = r as unknown as QuoteDetail;
    return {
      customerId: q.summary.customerId,
      contractId: q.contractId,
      siteId: q.siteId,
      serviceTypeId: q.serviceTypeId,
      issuedOn: q.summary.issuedOn,
      validUntil: q.summary.validUntil,
      currency: q.summary.currency,
      subject: q.summary.subject,
      lines: q.lines.map((l) => ({ serviceId: l.serviceId, description: l.description, quantity: l.quantity, unitName: l.unitName, unitPrice: l.unitPrice, discountPercent: l.discountPercent, vatRate: l.vatRate })),
      terms: q.terms,
      notes: q.notes,
    };
  },
  /** Lines without price take the price of the customer/contract price list (same rules as the tickets). */
  prepare: async (value, ctx) => {
    const lines = (value['lines'] as Record<string, unknown>[]) ?? [];
    const serviceIds = [...new Set(lines.map((l) => l['serviceId'] as string).filter(Boolean))];
    if (serviceIds.length === 0) {
      return value;
    }

    const prices = await firstValueFrom(
      ctx.api.get<ResolvedPrice[]>('pricing/resolve', { customerId: value['customerId'] as string, contractId: (value['contractId'] as string) ?? null, date: value['issuedOn'] as string, serviceIds }),
    ).catch(() => [] as ResolvedPrice[]);
    const byService = new Map(prices.map((p) => [p.serviceId, p]));
    return {
      ...value,
      lines: lines.map((line) => {
        const price = byService.get(line['serviceId'] as string);
        return {
          ...line,
          description: line['description'] ?? price?.name ?? '',
          unitName: line['unitName'] ?? price?.unitName ?? 'Servicio',
          unitPrice: line['unitPrice'] ?? price?.unitPrice ?? 0,
          discountPercent: line['discountPercent'] ?? 0,
          vatRate: line['vatRate'] ?? price?.vatRate ?? 0.16,
        };
      }),
    };
  },
  toPayload: (v) => ({ ...v }),
  title: (r) => (r['summary'] as QuoteSummary).folio,
  subtitle: (r, i18n) => {
    const q = r as unknown as QuoteDetail;
    return `${q.summary.customerName ?? ''} · ${i18n.t('fields.total')} ${money(q.summary.total, q.summary.currency)}`;
  },
  status: { enum: 'quoteStatus', value: (r) => (r['summary'] as QuoteSummary).status },
  warnings: (r) => {
    const q = r as unknown as QuoteDetail;
    return [q.rejectionReason ? `Rechazada: ${q.rejectionReason}` : null, q.workOrderFolio ? `Convertida en la orden ${q.workOrderFolio}` : null].filter((w): w is string => !!w);
  },
  actions: [
    {
      key: 'pdf',
      icon: 'picture_as_pdf',
      label: 'common.pdf',
      run: async (ctx, r) => {
        openFile(await firstValueFrom(ctx.api.download(`quotes/${r['id'] as string}/pdf`)));
        return false;
      },
    },
    { key: 'send', icon: 'send', label: 'quotes.actions.send', permissions: [P.quotesManage], visible: (r) => (r['summary'] as QuoteSummary).status === 'draft', run: (ctx, r) => quoteAction(ctx, r, 'send') },
    { key: 'accept', icon: 'thumb_up', label: 'quotes.actions.accept', permissions: [P.quotesManage], visible: (r) => (r['summary'] as QuoteSummary).status === 'sent', run: (ctx, r) => quoteAction(ctx, r, 'accept') },
    { key: 'reject', icon: 'thumb_down', label: 'quotes.actions.reject', permissions: [P.quotesManage], visible: (r) => (r['summary'] as QuoteSummary).status === 'sent', run: (ctx, r) => quoteAction(ctx, r, 'reject') },
    {
      key: 'convert',
      icon: 'event_note',
      label: 'quotes.actions.convert',
      primary: true,
      permissions: [P.quotesManage],
      visible: (r) => (r['summary'] as QuoteSummary).status === 'accepted',
      run: async (ctx, r) => {
        const q = r as unknown as QuoteDetail;
        const result = await ctx.form(
          {
            title: ctx.i18n.t('quotes.convertTitle', { folio: q.summary.folio }),
            fields: [
              { key: 'scheduledStart', label: 'fields.scheduledStart', type: 'datetime', required: true },
              { key: 'scheduledEnd', label: 'fields.scheduledEnd', type: 'datetime', required: true },
              { key: 'siteId', label: 'fields.site', type: 'lookup', catalog: 'catalogs/sites', required: true },
              { key: 'serviceTypeId', label: 'fields.serviceType', type: 'lookup', catalog: 'pricing/service-types', required: true },
            ],
            save: (api, v) =>
              api.post<string>(`quotes/${q.summary.id}/convert`, {
                scheduledStart: new Date(v['scheduledStart'] as string).toISOString(),
                scheduledEnd: new Date(v['scheduledEnd'] as string).toISOString(),
                siteId: v['siteId'],
                serviceTypeId: v['serviceTypeId'],
              }),
            width: '640px',
          },
          { scheduledStart: nowLocalInput(24), scheduledEnd: nowLocalInput(32), siteId: q.siteId, serviceTypeId: q.serviceTypeId },
        );
        return typeof result === 'string' ? `ordenes/${result}` : false;
      },
    },
  ],
};

async function quoteAction(ctx: ActionContext, record: Record<string, unknown>, action: 'send' | 'accept' | 'reject'): Promise<boolean> {
  let reason: string | null = null;
  if (action === 'reject') {
    reason = await ctx.dialogs.prompt(ctx.i18n.t('quotes.rejectTitle'), ctx.i18n.t('fields.reason'));
    if (!reason) {
      return false;
    }
  }

  await firstValueFrom(ctx.api.post(`quotes/${record['id'] as string}/status`, { action, reason }));
  return true;
}
