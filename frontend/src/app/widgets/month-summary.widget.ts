import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { from, of, switchMap } from 'rxjs';
import { BrocalApi } from '../core/api';
import { MoneyPipe } from '../core/format';
import { TPipe } from '../core/i18n';
import { MonthSummary } from '../core/models';
import { P } from '../core/permissions';
import { apiResource } from '../core/resource';
import { BrocalSession } from '../core/session';
import { WIDGET_STYLES, WidgetContext } from './widget-base';

/** ChronoLith dashboard widget: services of the month of the user's company and what is pending to invoice. */
@Component({
  selector: 'br-month-summary-widget',
  imports: [RouterLink, MatIconModule, MatProgressBarModule, MoneyPipe, TPipe],
  template: `
    @if (data.isLoading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (data.value(); as state) {
      @if (state === 'forbidden') {
        <p class="br-widget__message"><mat-icon>lock</mat-icon>{{ 'widgets.noAccess' | t }}</p>
      } @else {
        <div class="br-widget">
          <span class="br-widget__company">{{ session.tenant()?.name }}</span>
          <div class="br-widget__figures">
            <div class="br-widget__figure"><strong>{{ state.signedTickets }}</strong><span>{{ 'home.signedTickets' | t }}</span></div>
            <div class="br-widget__figure"><strong>{{ state.amount | money }}</strong><span>{{ 'home.amountMonth' | t }}</span></div>
            <div class="br-widget__figure"><strong>{{ state.pendingAmount | money }}</strong><span>{{ 'home.pendingInvoice' | t }} ({{ state.pendingInvoice }})</span></div>
            <div class="br-widget__figure"><strong>{{ state.openWorkOrders }}</strong><span>{{ 'home.openOrders' | t }}</span></div>
          </div>
          <a class="br-widget__more" [routerLink]="[moduleRoute(), 'inicio']">{{ 'widgets.open' | t }}</a>
        </div>
      }
    } @else if (data.error()) {
      <p class="br-widget__message"><mat-icon>error</mat-icon>{{ 'widgets.error' | t }}</p>
    }
  `,
  styles: WIDGET_STYLES,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MonthSummaryWidget {
  private readonly api = inject(BrocalApi);
  private readonly context = inject(WidgetContext);
  protected readonly session = inject(BrocalSession);

  readonly settings = input<Record<string, unknown> | null>(null);
  readonly moduleRoute = input('/brocal');

  protected readonly data = apiResource({
    stream: () =>
      from(this.context.ensure()).pipe(
        switchMap(() => (this.session.can(P.dashboardRead, P.billingRead, P.ticketsRead) ? this.api.get<MonthSummary>('dashboard/month-summary') : of('forbidden' as const))),
      ),
  });
}

export default MonthSummaryWidget;
