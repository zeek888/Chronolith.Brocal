import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { from, of, switchMap } from 'rxjs';
import { BrocalApi } from '../core/api';
import { DatePipe } from '../core/format';
import { TPipe } from '../core/i18n';
import { ComplianceDocument } from '../core/models';
import { P } from '../core/permissions';
import { apiResource } from '../core/resource';
import { BrocalSession } from '../core/session';
import { StatusChip } from '../shared/ui';
import { WIDGET_STYLES, WidgetContext } from './widget-base';

/** ChronoLith dashboard widget: unit permits and personnel documents expired or about to expire. Settings: `{ "dias": 30 }`. */
@Component({
  selector: 'br-expiries-widget',
  imports: [RouterLink, MatIconModule, MatProgressBarModule, DatePipe, TPipe, StatusChip],
  template: `
    @if (data.isLoading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (data.value(); as state) {
      @if (state === 'forbidden') {
        <p class="br-widget__message"><mat-icon>lock</mat-icon>{{ 'widgets.noAccess' | t }}</p>
      } @else {
        <div class="br-widget">
          <span class="br-widget__company">{{ session.tenant()?.name }} · {{ 'widgets.nextDays' | t: { n: days() } }}</span>
          <ul class="br-widget__list">
            @for (doc of state; track doc.id) {
              <li>
                <mat-icon>{{ doc.subject === 'unit' ? 'local_shipping' : 'badge' }}</mat-icon>
                <span class="grow"><strong>{{ doc.subjectCode }}</strong> · {{ doc.documentTypeName }}</span>
                <span>{{ doc.expiresOn | d: 'short' }}</span>
                <br-status enum="complianceStatus" [value]="doc.status" />
              </li>
            } @empty {
              <li class="br-widget__message"><mat-icon>verified</mat-icon>{{ 'home.noExpiring' | t }}</li>
            }
          </ul>
          <a class="br-widget__more" [routerLink]="[moduleRoute(), 'personal', 'matriz']">{{ 'widgets.viewMatrix' | t }}</a>
        </div>
      }
    } @else if (data.error()) {
      <p class="br-widget__message"><mat-icon>error</mat-icon>{{ 'widgets.error' | t }}</p>
    }
  `,
  styles: WIDGET_STYLES,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ExpiriesWidget {
  private readonly api = inject(BrocalApi);
  private readonly context = inject(WidgetContext);
  protected readonly session = inject(BrocalSession);

  readonly settings = input<Record<string, unknown> | null>(null);
  readonly moduleRoute = input('/brocal');

  protected readonly days = computed(() => {
    const days = Number(this.settings()?.['dias']);
    return Number.isInteger(days) && days > 0 ? days : 30;
  });

  protected readonly data = apiResource({
    params: () => this.days(),
    stream: ({ params: days }) =>
      from(this.context.ensure()).pipe(
        switchMap(() =>
          this.session.can(P.permitsRead, P.permitsManage, P.personnelRead, P.personnelManage, P.fleetRead, P.dashboardRead)
            ? this.api.get<ComplianceDocument[]>('compliance/expiring', { days, take: 8 })
            : of('forbidden' as const),
        ),
      ),
  });
}

export default ExpiriesWidget;
