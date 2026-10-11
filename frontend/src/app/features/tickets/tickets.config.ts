import { firstValueFrom } from 'rxjs';
import { PagedResult, TicketSummary } from '../../core/models';
import { P } from '../../core/permissions';
import { ListConfig } from '../../shared/crud/list-page';

const STATUSES = ['draft', 'signed', 'approved', 'invoiced', 'cancelled'] as const;

export const TICKET_LIST: ListConfig<TicketSummary> = {
  key: 'tickets',
  icon: 'receipt_long',
  api: 'tickets',
  link: (r) => ['boletas', r.id],
  exportDataset: 'tickets',
  create: { permissions: [P.ticketsCapture], path: 'boletas/nueva', label: 'tickets.newTitle' },
  headerActions: [
    { key: 'pending', icon: 'cloud_upload', label: 'tickets.pending', path: 'boletas/pendientes', permissions: [P.ticketsCapture] },
    {
      key: 'approve',
      icon: 'verified',
      label: 'tickets.approveSigned',
      permissions: [P.ticketsApprove],
      run: async (ctx, filters) => {
        const page = await firstValueFrom(ctx.api.get<PagedResult<TicketSummary>>('tickets', { ...filters, status: ['signed'], pageSize: 200 }));
        if (page.items.length === 0) {
          ctx.dialogs.info(ctx.i18n.t('tickets.noSignedToApprove'));
          return false;
        }

        const ok = await ctx.dialogs.confirm(ctx.i18n.t('tickets.approveSigned'), ctx.i18n.t('tickets.approveSignedMessage', { n: page.items.length }));
        if (!ok) {
          return false;
        }

        const approved = await firstValueFrom(ctx.api.post<number>('tickets/approve', { ids: page.items.map((t) => t.id) }));
        ctx.dialogs.success(ctx.i18n.t('tickets.approvedCount', { n: approved }));
        return true;
      },
    },
  ],
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'ticketStatus', values: STATUSES, multiple: true },
    { key: 'customerId', label: 'fields.customer', type: 'lookup', catalog: 'catalogs/customers' },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
    { key: 'uninvoiced', label: 'tickets.uninvoicedOnly', type: 'toggle' },
  ],
  columns: [
    { key: 'folio', label: 'fields.folio', sub: (r) => r.serviceTypeName },
    { key: 'serviceDate', label: 'fields.serviceDate', type: 'date' },
    { key: 'customerName', label: 'fields.customer', sub: (r) => r.siteName },
    { key: 'workOrderFolio', label: 'fields.workOrder', wide: true },
    { key: 'capturedByName', label: 'fields.capturedBy', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'ticketStatus' },
    { key: 'total', label: 'fields.total', type: 'money', align: 'end', currency: (r) => r.currency },
  ],
  muted: (r) => r.status === 'cancelled',
};
