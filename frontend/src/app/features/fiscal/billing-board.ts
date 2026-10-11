import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { BrocalApi } from '../../core/api';
import { DatePipe, MoneyPipe, firstOfMonth, today } from '../../core/format';
import { I18n, TPipe, activeLocale } from '../../core/i18n';
import { Aging, BillingBoard, Lookup } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { CanDirective } from '../../core/session';
import { Chart, ChartSpec } from '../../shared/chart';
import { LookupSelect } from '../../shared/lookup-select';
import { KpiCard, PageHeader, StatusChip } from '../../shared/ui';

/** Tablero de facturación: executed vs invoiced vs collected, by month and customer, with tickets waiting to be invoiced. */
@Component({
  selector: 'br-billing-board',
  imports: [FormsModule, RouterLink, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TPipe, MoneyPipe, DatePipe, CanDirective, Chart, KpiCard, LookupSelect, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.billingBoard' | t" icon="query_stats" [subtitle]="'billing.hint' | t">
        <a *brCan="p.invoicingIssue" mat-flat-button [routerLink]="nav.to('facturacion/nueva')"><mat-icon>post_add</mat-icon>{{ 'nav.invoiceTickets' | t }}</a>
      </br-page-header>

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
        <br-lookup class="br-filter--lookup" catalog="catalogs/customers" [label]="'fields.customer' | t" (selected)="customer.set(idOf($event))" />
      </div>

      @if (board.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (board.value(); as b) {
        <div class="br-kpis">
          <br-kpi [label]="'billing.executed' | t" [value]="b.executed" format="money" icon="engineering" />
          <br-kpi [label]="'billing.uninvoiced' | t" [value]="b.uninvoiced" format="money" icon="pending_actions" [tone]="b.alertTickets > 0 ? 'warning' : 'neutral'" [detail]="'billing.ticketsWithoutInvoice' | t: { n: b.ticketsWithoutInvoice }" />
          <br-kpi [label]="'billing.invoiced' | t" [value]="b.invoiced" format="money" icon="request_page" tone="info" />
          <br-kpi [label]="'billing.collected' | t" [value]="b.collected" format="money" icon="paid" tone="success" />
          <br-kpi [label]="'billing.receivable' | t" [value]="b.receivable" format="money" icon="account_balance_wallet" />
        </div>

        @if (b.alertTickets > 0) {
          <div class="br-warning-box"><mat-icon>notification_important</mat-icon>{{ 'billing.alert' | t: { n: b.alertTickets, days: b.alertDays } }}</div>
        }

        <div class="br-grid br-grid--2">
          <section class="br-card">
            <h2>{{ 'billing.byMonth' | t }}</h2>
            <br-chart [spec]="monthsChart()" />
          </section>
          <section class="br-card">
            <h2>{{ 'billing.pending' | t }}</h2>
            <div class="cl-table-wrapper br-scroll">
              <table class="br-table">
                <tbody>
                  @for (t of b.pending; track t.id) {
                    <tr>
                      <td><a class="cl-row-link" [routerLink]="nav.to('boletas', t.id)">{{ t.folio }}</a><div class="br-sub">{{ t.customerName }}</div></td>
                      <td>{{ t.serviceDate | d }}</td>
                      <td><br-status enum="ticketStatus" [value]="t.status" /></td>
                      <td class="br-end">
                        <span class="cl-chip" [class.cl-chip--error]="t.alert">{{ 'billing.daysWaiting' | t: { n: t.daysWaiting } }}</span>
                      </td>
                      <td class="br-end">{{ t.total | money }}</td>
                    </tr>
                  } @empty {
                    <tr><td class="br-table-empty">{{ 'billing.allInvoiced' | t }}</td></tr>
                  }
                </tbody>
              </table>
            </div>
          </section>
        </div>

        <section class="br-card">
          <h2>{{ 'billing.byCustomer' | t }}</h2>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'fields.customer' | t }}</th>
                  <th class="br-end">{{ 'billing.executed' | t }}</th>
                  <th class="br-end">{{ 'billing.uninvoiced' | t }}</th>
                  <th class="br-end">{{ 'billing.invoiced' | t }}</th>
                  <th class="br-end">{{ 'billing.collected' | t }}</th>
                  <th class="br-end">{{ 'billing.receivable' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (c of b.customers; track c.customerId) {
                  <tr>
                    <td>{{ c.customerName }}</td>
                    <td class="br-end">{{ c.executed | money }}</td>
                    <td class="br-end">{{ c.uninvoiced | money }}</td>
                    <td class="br-end">{{ c.invoiced | money }}</td>
                    <td class="br-end">{{ c.collected | money }}</td>
                    <td class="br-end">{{ c.receivable | money }}</td>
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
    .br-scroll { max-height: 320px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BillingBoardPage {
  private readonly api = inject(BrocalApi);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;

  protected readonly from = signal(firstOfMonth(-5));
  protected readonly to = signal(today());
  protected readonly customer = signal<string | null>(null);

  protected readonly board = apiResource({
    params: () => ({ from: this.from(), to: this.to(), customerId: this.customer() }),
    stream: ({ params }) => this.api.get<BillingBoard>('billing/board', params),
  });

  protected readonly monthsChart = computed<ChartSpec>(() => {
    const months = this.board.value()?.months ?? [];
    const format = new Intl.DateTimeFormat(activeLocale(), { month: 'short', year: '2-digit' });
    return {
      kind: 'bar',
      money: true,
      categories: months.map((m) => format.format(new Date(m.year, m.month - 1, 1))),
      series: [
        { name: this.i18n.t('billing.executed'), data: months.map((m) => m.executed) },
        { name: this.i18n.t('billing.invoiced'), data: months.map((m) => m.invoiced) },
        { name: this.i18n.t('billing.collected'), data: months.map((m) => m.collected) },
      ],
    };
  });

  protected idOf(selected: Lookup | Lookup[] | null): string | null {
    return Array.isArray(selected) ? null : (selected?.id ?? null);
  }
}

/** Antigüedad de saldos by customer. */
@Component({
  selector: 'br-aging',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, MoneyPipe, DatePipe, Chart, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.aging' | t" icon="hourglass_bottom" [subtitle]="'aging.hint' | t">
        <a mat-stroked-button [routerLink]="nav.to('cobranza/pagos')"><mat-icon>paid</mat-icon>{{ 'nav.payments' | t }}</a>
        <a mat-stroked-button [routerLink]="nav.to('cobranza/gestion')"><mat-icon>support_agent</mat-icon>{{ 'nav.collectionNotes' | t }}</a>
      </br-page-header>
      @if (aging.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (aging.value(); as a) {
        <section class="br-card">
          <br-chart [spec]="chart()" height="220px" />
        </section>
        <div class="cl-table-wrapper">
          <table class="br-table">
            <thead>
              <tr>
                <th>{{ 'fields.customer' | t }}</th>
                <th class="br-end">{{ 'aging.current' | t }}</th>
                <th class="br-end">1-30</th>
                <th class="br-end">31-60</th>
                <th class="br-end">61-90</th>
                <th class="br-end">+90</th>
                <th class="br-end">{{ 'fields.total' | t }}</th>
                <th class="br-wide">{{ 'aging.oldest' | t }}</th>
                <th class="br-wide">{{ 'aging.promise' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of a.rows; track row.customerId) {
                <tr>
                  <td><a class="cl-row-link" [routerLink]="nav.to('facturacion/facturas')" [queryParams]="{ customerId: row.customerId, onlyWithBalance: true }">{{ row.customerName }}</a><div class="br-sub">{{ 'aging.invoices' | t: { n: row.invoices } }}</div></td>
                  <td class="br-end">{{ row.current | money }}</td>
                  <td class="br-end">{{ row.days1To30 | money }}</td>
                  <td class="br-end">{{ row.days31To60 | money }}</td>
                  <td class="br-end">{{ row.days61To90 | money }}</td>
                  <td class="br-end">{{ row.over90 | money }}</td>
                  <td class="br-end"><strong>{{ row.total | money }}</strong></td>
                  <td class="br-wide">{{ row.oldestDueOn | d }}</td>
                  <td class="br-wide">{{ row.nextPromise | d }}</td>
                </tr>
              } @empty {
                <tr><td colspan="9" class="br-table-empty">{{ 'aging.empty' | t }}</td></tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td>{{ 'fields.total' | t }}</td>
                <td class="br-end">{{ a.totals.current | money }}</td>
                <td class="br-end">{{ a.totals.days1To30 | money }}</td>
                <td class="br-end">{{ a.totals.days31To60 | money }}</td>
                <td class="br-end">{{ a.totals.days61To90 | money }}</td>
                <td class="br-end">{{ a.totals.over90 | money }}</td>
                <td class="br-end">{{ a.totals.total | money }}</td>
                <td class="br-wide"></td>
                <td class="br-wide"></td>
              </tr>
            </tfoot>
          </table>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgingPage {
  private readonly api = inject(BrocalApi);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);
  protected readonly aging = apiResource({ stream: () => this.api.get<Aging>('collections/aging') });

  protected readonly chart = computed<ChartSpec>(() => {
    const t = this.aging.value()?.totals;
    return {
      kind: 'bar',
      money: true,
      categories: [this.i18n.t('aging.current'), '1-30', '31-60', '61-90', '+90'],
      series: [{ name: this.i18n.t('fields.balance'), data: t ? [t.current, t.days1To30, t.days31To60, t.days61To90, t.over90] : [] }],
    };
  });
}
