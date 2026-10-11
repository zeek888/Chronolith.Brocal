import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, openFile, saveFile } from '../../core/api';
import { DatePipe, MoneyPipe, NumPipe, firstOfMonth, today } from '../../core/format';
import { I18n, TPipe, activeLocale } from '../../core/i18n';
import { Dashboard, InvoiceSummary, Lookup, PagedResult, PortalSummary, ReportFormat } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { Chart, ChartSpec } from '../../shared/chart';
import { LookupSelect } from '../../shared/lookup-select';
import { EmptyState, KpiCard, PageHeader, StatusChip } from '../../shared/ui';

/**
 * Dashboard gerencial: unit utilization, revenue per unit, average days from ticket to invoice, overdue
 * receivables and services of the month, plus revenue by service type and customer, tickets per day and fleet.
 */
@Component({
  selector: 'br-dashboard',
  imports: [FormsModule, RouterLink, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TPipe, MoneyPipe, NumPipe, Chart, KpiCard, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.dashboard' | t" icon="monitoring" [subtitle]="'dashboard.hint' | t" />
      <div class="cl-toolbar-row br-filters">
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.from' | t }}</mat-label>
          <input matInput [matDatepicker]="fromPicker" [ngModel]="from()" (ngModelChange)="from.set($event)" />
          <mat-datepicker-toggle matIconSuffix [for]="fromPicker" />
          <mat-datepicker #fromPicker />
        </mat-form-field>
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.to' | t }}</mat-label>
          <input matInput [matDatepicker]="toPicker" [ngModel]="to()" (ngModelChange)="to.set($event)" />
          <mat-datepicker-toggle matIconSuffix [for]="toPicker" />
          <mat-datepicker #toPicker />
        </mat-form-field>
      </div>
      @if (data.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (data.value(); as d) {
        <div class="br-kpis">
          <br-kpi [label]="'dashboard.utilization' | t" [value]="d.unitUtilizationPercent" format="percent" icon="local_shipping" [hint]="'dashboard.utilizationHint' | t" />
          <br-kpi [label]="'dashboard.revenuePerUnit' | t" [value]="d.revenuePerUnit" format="money" icon="payments" />
          <br-kpi [label]="'dashboard.daysToInvoice' | t" [value]="d.averageDaysTicketToInvoice" format="days" icon="schedule" [tone]="d.averageDaysTicketToInvoice > 15 ? 'warning' : 'success'" />
          <br-kpi [label]="'dashboard.overdue' | t" [value]="d.overdueReceivables" format="money" icon="warning" [tone]="d.overdueReceivables > 0 ? 'error' : 'success'" />
          <br-kpi
            [label]="'dashboard.servicesMonth' | t"
            [value]="d.servicesThisMonth"
            icon="receipt_long"
            tone="info"
            [detail]="(d.servicesAmountThisMonth | money) + ' · ' + ('dashboard.previous' | t: { n: d.servicesPreviousMonth })"
          />
        </div>

        <div class="br-grid br-grid--2">
          <section class="br-card">
            <h2>{{ 'dashboard.byServiceType' | t }}</h2>
            <br-chart [spec]="serviceTypes()" />
          </section>
          <section class="br-card">
            <h2>{{ 'dashboard.byCustomer' | t }}</h2>
            <br-chart [spec]="customers()" />
          </section>
          <section class="br-card">
            <h2>{{ 'dashboard.ticketsPerDay' | t }}</h2>
            <br-chart [spec]="perDay()" />
          </section>
          <section class="br-card">
            <h2>{{ 'dashboard.aging' | t }}</h2>
            <br-chart [spec]="aging()" />
          </section>
        </div>

        <section class="br-card">
          <div class="br-card__header">
            <h2>{{ 'dashboard.units' | t }}</h2>
            <div class="br-chips">
              @for (s of fleet(); track s.status) {
                <span class="cl-chip">{{ 'enums.unitStatus.' + s.status | t }}: {{ s.count }}</span>
              }
            </div>
          </div>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'fields.unit' | t }}</th>
                  <th>{{ 'fields.status' | t }}</th>
                  <th class="br-end">{{ 'dashboard.daysWorked' | t }}</th>
                  <th>{{ 'dashboard.utilization' | t }}</th>
                  <th class="br-end">{{ 'dashboard.revenue' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (u of d.units; track u.unitId) {
                  <tr>
                    <td><a class="cl-row-link" [routerLink]="nav.to('flota/unidades', u.unitId)">{{ u.code }}</a><div class="br-sub">{{ u.name }}</div></td>
                    <td><br-status enum="unitStatus" [value]="u.status" /></td>
                    <td class="br-end">{{ u.daysWorked }}</td>
                    <td>
                      <div class="br-bar"><span [style.width.%]="u.utilizationPercent"></span></div>
                      <small>{{ u.utilizationPercent | num: 1 }} %</small>
                    </td>
                    <td class="br-end">{{ u.revenue | money }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .br-bar { height: 6px; border-radius: 3px; background: var(--mat-sys-surface-container-highest); overflow: hidden; min-width: 90px; }
    .br-bar span { display: block; height: 100%; background: var(--mat-sys-primary); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly api = inject(BrocalApi);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);

  protected readonly from = signal(firstOfMonth(0));
  protected readonly to = signal(today());
  protected readonly data = apiResource({
    params: () => ({ from: this.from(), to: this.to() }),
    stream: ({ params }) => this.api.get<Dashboard>('dashboard', params),
  });

  protected readonly serviceTypes = computed<ChartSpec>(() => ({ kind: 'pie', money: true, items: (this.data.value()?.revenueByServiceType ?? []).map((k) => ({ name: k.label, value: k.value })) }));
  protected readonly customers = computed<ChartSpec>(() => {
    const items = this.data.value()?.revenueByCustomer ?? [];
    return { kind: 'bar', money: true, horizontal: true, categories: items.map((k) => k.label), series: [{ name: this.i18n.t('dashboard.revenue'), data: items.map((k) => k.value) }] };
  });
  protected readonly perDay = computed<ChartSpec>(() => {
    const items = this.data.value()?.ticketsByDay ?? [];
    const format = new Intl.DateTimeFormat(activeLocale(), { day: '2-digit', month: 'short', timeZone: 'UTC' });
    return { kind: 'line', categories: items.map((k) => format.format(new Date(`${k.label}T00:00:00Z`))), series: [{ name: this.i18n.t('nav.tickets'), data: items.map((k) => k.value) }] };
  });
  protected readonly aging = computed<ChartSpec>(() => {
    const items = this.data.value()?.aging ?? [];
    return { kind: 'bar', money: true, categories: items.map((k) => k.label), series: [{ name: this.i18n.t('fields.balance'), data: items.map((k) => k.value) }] };
  });
  protected readonly fleet = computed(() => Object.entries(this.data.value()?.fleetStatus ?? {}).map(([status, count]) => ({ status: status.charAt(0).toLowerCase() + status.slice(1), count })));
}

/** Reporte mensual por cliente o campo, en PDF o Excel con la marca de la empresa. Customers (portal) get their own. */
@Component({
  selector: 'br-reports',
  imports: [ReactiveFormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, TPipe, LookupSelect, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.reports' | t" icon="summarize" [subtitle]="'reports.hint' | t" />
      <section class="br-card">
        <h2>{{ 'reports.monthly' | t }}</h2>
        <div class="cl-form-grid">
          <mat-form-field>
            <mat-label>{{ 'reports.month' | t }}</mat-label>
            <input matInput type="month" [formControl]="month" />
          </mat-form-field>
          @if (!session.isPortalUser()) {
            <br-lookup [formControl]="customer" catalog="catalogs/customers" [label]="'fields.customer' | t" [hint]="'reports.allCustomers' | t" />
          }
          <mat-form-field>
            <mat-label>{{ 'fields.field' | t }}</mat-label>
            <mat-select [formControl]="field">
              <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
              @for (f of fields.value() ?? []; track f) {
                <mat-option [value]="f">{{ f }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-button-toggle-group [formControl]="format">
            <mat-button-toggle value="pdf"><mat-icon>picture_as_pdf</mat-icon> PDF</mat-button-toggle>
            <mat-button-toggle value="excel"><mat-icon>table_view</mat-icon> Excel</mat-button-toggle>
          </mat-button-toggle-group>
        </div>
        @if (busy()) {
          <mat-progress-bar mode="indeterminate" />
        }
        <div class="br-actions" style="margin-top: 12px">
          <span class="cl-spacer"></span>
          <button mat-flat-button type="button" (click)="generate()" [disabled]="busy() || !month.value"><mat-icon>download</mat-icon>{{ 'reports.generate' | t }}</button>
        </div>
      </section>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPage {
  private readonly api = inject(BrocalApi);
  protected readonly session = inject(BrocalSession);
  protected readonly month = new FormControl(today().slice(0, 7));
  protected readonly customer = new FormControl<string | null>(null);
  protected readonly field = new FormControl<string | null>(null);
  protected readonly format = new FormControl<ReportFormat>('pdf', { nonNullable: true });
  protected readonly busy = signal(false);
  protected readonly fields = apiResource({ stream: () => this.api.get<string[]>('reports/fields') });

  protected async generate(): Promise<void> {
    const [year, month] = (this.month.value ?? '').split('-').map(Number);
    this.busy.set(true);
    try {
      const file = await firstValueFrom(this.api.download('reports/monthly', { year, month, customerId: this.customer.value, field: this.field.value, format: this.format.value }));
      if (this.format.value === 'pdf') {
        openFile(file);
      } else {
        saveFile(file);
      }
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}

/**
 * Portal del cliente: services of the month, scheduled work, account status, recent tickets and invoices. Portal
 * users see their own customer; the office previews the portal of the customer it chooses.
 */
@Component({
  selector: 'br-portal',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, MoneyPipe, DatePipe, EmptyState, KpiCard, LookupSelect, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="summary.value()?.customerName ?? ('nav.portal' | t)" icon="storefront" [subtitle]="'portal.hint' | t">
        @if (!session.isPortalUser()) {
          <br-lookup class="br-filter--lookup" catalog="catalogs/customers" [label]="'fields.customer' | t" [value]="customerId()" (selected)="customerId.set(idOf($event))" />
        }
        @if (session.can(p.reportsRead, p.portalAccess)) {
          <a mat-stroked-button [routerLink]="nav.to('reportes')"><mat-icon>summarize</mat-icon>{{ 'nav.reports' | t }}</a>
        }
      </br-page-header>

      @if (!session.isPortalUser() && !customerId()) {
        <br-empty icon="storefront" [title]="'portal.chooseCustomer' | t" [message]="'portal.chooseCustomerHint' | t" />
      } @else if (summary.value(); as p) {
        <div class="br-kpis">
          <br-kpi [label]="'portal.ticketsThisMonth' | t" [value]="p.ticketsThisMonth" icon="receipt_long" [detail]="'portal.lastMonth' | t: { n: p.ticketsLastMonth }" />
          <br-kpi [label]="'portal.amountThisMonth' | t" [value]="p.amountThisMonth" format="money" icon="payments" />
          <br-kpi [label]="'portal.scheduledOrders' | t" [value]="p.scheduledOrders" icon="event" />
          <br-kpi [label]="'portal.openInvoices' | t" [value]="p.openInvoices" icon="request_page" />
          <br-kpi [label]="'portal.balance' | t" [value]="p.balance" format="money" icon="account_balance_wallet" [tone]="p.overdueBalance > 0 ? 'warning' : 'neutral'" />
        </div>
        <div class="br-grid br-grid--2">
          <section class="br-card">
            <div class="br-card__header">
              <h2>{{ 'portal.recentTickets' | t }}</h2>
              <a mat-button [routerLink]="nav.to('boletas')">{{ 'common.viewAll' | t }}</a>
            </div>
            <table class="br-table">
              <tbody>
                @for (t of p.recentTickets; track t.id) {
                  <tr>
                    <td><a class="cl-row-link br-nowrap" [routerLink]="nav.to('boletas', t.id)">{{ t.folio }}</a><div class="br-sub">{{ t.siteName }}</div></td>
                    <td class="br-nowrap">{{ t.serviceDate | d }}</td>
                    <td><br-status enum="ticketStatus" [value]="t.status" /></td>
                    <td class="br-end">{{ t.total | money }}</td>
                  </tr>
                } @empty {
                  <tr><td class="br-table-empty">{{ 'common.noRecords' | t }}</td></tr>
                }
              </tbody>
            </table>
          </section>
          @if (canSeeInvoices()) {
            <section class="br-card">
              <div class="br-card__header">
                <h2>{{ 'portal.invoices' | t }}</h2>
                <a mat-button [routerLink]="nav.to('facturacion/facturas')">{{ 'common.viewAll' | t }}</a>
              </div>
              <table class="br-table">
                <tbody>
                  @for (i of invoices.value()?.items ?? []; track i.id) {
                    <tr>
                      <td><a class="cl-row-link" [routerLink]="nav.to('facturacion/facturas', i.id)">{{ i.series }}-{{ i.folio }}</a><div class="br-sub">{{ i.copadeNumber }}</div></td>
                      <td>{{ i.dueOn | d }}</td>
                      <td class="br-end">{{ i.total | money }}</td>
                      <td class="br-end">{{ i.balance | money }}</td>
                    </tr>
                  } @empty {
                    <tr><td class="br-table-empty">{{ 'common.noRecords' | t }}</td></tr>
                  }
                </tbody>
              </table>
            </section>
          }
        </div>
      } @else if (summary.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalPage {
  private readonly api = inject(BrocalApi);
  protected readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;
  protected readonly customerId = signal<string | null>(null);
  protected readonly canSeeInvoices = computed(() => this.session.can(P.invoicingRead, P.invoicingIssue, P.collectionsRead, P.billingRead, P.portalAccess));

  /** Portal users: their own customer (the API ignores the parameter). Office: the chosen one, nothing until then. */
  private readonly customer = computed(() => (this.session.isPortalUser() ? { customerId: null } : this.customerId() ? { customerId: this.customerId() } : undefined));

  protected readonly summary = apiResource({
    params: () => this.customer(),
    stream: ({ params }) => this.api.get<PortalSummary>('portal/summary', params),
  });

  protected readonly invoices = apiResource({
    params: () => (this.canSeeInvoices() ? this.customer() : undefined),
    stream: ({ params }) => this.api.get<PagedResult<InvoiceSummary>>('invoices', { ...params, pageSize: 8 }),
  });

  protected idOf(selected: Lookup | Lookup[] | null): string | null {
    return Array.isArray(selected) ? null : (selected?.id ?? null);
  }
}
