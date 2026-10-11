import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { BrocalApi } from '../../core/api';
import { DatePipe, MoneyPipe, today } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { ComplianceDocument, MaintenanceDue, MonthSummary, PagedResult, PortalSummary, TicketSummary, WorkOrderSummary } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { TicketOutbox } from '../../core/offline/ticket-outbox';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { EmptyState, KpiCard, PageHeader, StatusChip } from '../../shared/ui';

interface QuickAction {
  key: string;
  icon: string;
  path: string;
  permissions: string[];
  module?: string;
}

const QUICK_ACTIONS: QuickAction[] = [
  { key: 'newTicket', icon: 'add_circle', path: 'boletas/nueva', permissions: [P.ticketsCapture], module: 'tickets' },
  { key: 'newWorkOrder', icon: 'event', path: 'ordenes/nueva', permissions: [P.dispatchManage], module: 'dispatch' },
  { key: 'fuelLoad', icon: 'local_gas_station', path: 'combustible/cargas', permissions: [P.fuelCapture, P.fuelManage], module: 'fuel' },
  { key: 'reportIncident', icon: 'report', path: 'hse/incidentes/nuevo', permissions: [P.hseReport, P.hseManage], module: 'hse' },
  { key: 'inspection', icon: 'fact_check', path: 'hse/inspecciones/nueva', permissions: [P.hseReport, P.hseManage], module: 'hse' },
  { key: 'attendance', icon: 'how_to_reg', path: 'cuadrillas/asistencia', permissions: [P.crewsAttendance], module: 'crews' },
  { key: 'expense', icon: 'receipt', path: 'viaticos/nuevo', permissions: [P.expensesCapture], module: 'expenses' },
  { key: 'invoiceTickets', icon: 'post_add', path: 'facturacion/nueva', permissions: [P.invoicingIssue], module: 'invoicing' },
  { key: 'newQuote', icon: 'request_quote', path: 'cotizaciones/nueva', permissions: [P.quotesManage], module: 'quotes' },
];

/**
 * Home of each user: field staff see their orders of the day and their tickets, the office the month and
 * pending invoicing, management the key figures, customers (portal) their account; plus expiries and
 * maintenance due for whoever handles them.
 */
@Component({
  selector: 'br-home',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, MoneyPipe, DatePipe, EmptyState, KpiCard, PageHeader, StatusChip],
  templateUrl: './home.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomePage {
  private readonly api = inject(BrocalApi);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly outbox = inject(TicketOutbox);
  private readonly i18n = inject(I18n);
  protected readonly p = P;

  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const key = hour < 12 ? 'home.goodMorning' : hour < 19 ? 'home.goodAfternoon' : 'home.goodEvening';
    const name = this.session.tenant()?.membership.fullName ?? this.session.user()?.fullName ?? '';
    return this.i18n.t(key, { name: name.split(' ')[0] });
  });

  protected readonly actions = computed(() => QUICK_ACTIONS.filter((a) => (!a.module || this.session.hasModule(a.module)) && this.session.can(...a.permissions)));

  protected readonly summary = apiResource({
    params: () => (this.session.can(P.dashboardRead, P.billingRead, P.ticketsRead) && !this.session.isPortalUser() ? true : undefined),
    stream: () => this.api.get<MonthSummary>('dashboard/month-summary'),
  });

  protected readonly portal = apiResource({
    params: () => (this.session.isPortalUser() ? true : undefined),
    stream: () => this.api.get<PortalSummary>('portal/summary'),
  });

  protected readonly myOrders = apiResource({
    params: () => (this.session.can(P.dispatchReadOwn) && !this.session.can(P.dispatchRead) ? today() : undefined),
    stream: ({ params }) =>
      this.api.get<PagedResult<WorkOrderSummary>>('work-orders', { from: `${params}T00:00:00`, to: `${params}T23:59:59`, status: ['scheduled', 'dispatched', 'inProgress'], pageSize: 20 }),
  });

  protected readonly myTickets = apiResource({
    params: () => (this.session.can(P.ticketsReadOwn) && !this.session.can(P.ticketsRead) ? true : undefined),
    stream: () => this.api.get<PagedResult<TicketSummary>>('tickets', { pageSize: 8 }),
  });

  protected readonly expiring = apiResource({
    params: () => (this.session.can(P.permitsRead, P.permitsManage, P.personnelRead, P.personnelManage) ? true : undefined),
    stream: () => this.api.get<ComplianceDocument[]>('compliance/expiring', { days: 30, take: 8 }),
  });

  protected readonly maintenance = apiResource({
    params: () => (this.session.hasModule('maintenance') && this.session.can(P.maintenanceRead, P.maintenanceManage) ? true : undefined),
    stream: () => this.api.get<MaintenanceDue[]>('maintenance/due', { onlyDue: true }),
  });

  protected readonly hasCompany = computed(() => !!this.session.tenant());

  protected link(path: string, id?: string): string[] {
    return this.nav.to(path, id);
  }

}
