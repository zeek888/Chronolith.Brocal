import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { from, map, of, switchMap } from 'rxjs';
import { BrocalApi } from '../core/api';
import { TPipe } from '../core/i18n';
import { PagedResult, UnitInfo, UnitStatus } from '../core/models';
import { P } from '../core/permissions';
import { apiResource } from '../core/resource';
import { BrocalSession } from '../core/session';
import { toneOf } from '../shared/ui';
import { WIDGET_STYLES, WidgetContext } from './widget-base';

const STATUSES: UnitStatus[] = ['operating', 'available', 'workshop', 'outOfService'];

/** ChronoLith dashboard widget: units operating, available, in the workshop and out of service. */
@Component({
  selector: 'br-fleet-widget',
  imports: [RouterLink, MatIconModule, MatProgressBarModule, TPipe],
  template: `
    @if (data.isLoading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (data.value(); as state) {
      @if (state === 'forbidden') {
        <p class="br-widget__message"><mat-icon>lock</mat-icon>{{ 'widgets.noAccess' | t }}</p>
      } @else {
        <div class="br-widget">
          <span class="br-widget__company">{{ session.tenant()?.name }} · {{ 'widgets.units' | t: { n: state.total } }}</span>
          <div class="br-widget__figures">
            @for (s of state.counts; track s.status) {
              <div class="br-widget__figure">
                <strong>{{ s.count }}</strong>
                <span class="cl-chip" [class]="'cl-chip cl-chip--' + tone(s.status)">{{ 'enums.unitStatus.' + s.status | t }}</span>
              </div>
            }
          </div>
          @if (state.workshop.length > 0) {
            <ul class="br-widget__list">
              @for (unit of state.workshop; track unit.id) {
                <li><mat-icon>build</mat-icon><span class="grow"><strong>{{ unit.code }}</strong> · {{ unit.name }}</span></li>
              }
            </ul>
          }
          <a class="br-widget__more" [routerLink]="[moduleRoute(), 'flota', 'unidades']">{{ 'widgets.viewFleet' | t }}</a>
        </div>
      }
    } @else if (data.error()) {
      <p class="br-widget__message"><mat-icon>error</mat-icon>{{ 'widgets.error' | t }}</p>
    }
  `,
  styles: WIDGET_STYLES,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FleetWidget {
  private readonly api = inject(BrocalApi);
  private readonly context = inject(WidgetContext);
  protected readonly session = inject(BrocalSession);

  readonly settings = input<Record<string, unknown> | null>(null);
  readonly moduleRoute = input('/brocal');

  protected readonly data = apiResource({
    stream: () =>
      from(this.context.ensure()).pipe(
        switchMap(() =>
          this.session.hasModule('fleet') && this.session.can(P.fleetRead, P.fleetManage, P.maintenanceRead, P.dispatchManage)
            ? this.api.get<PagedResult<UnitInfo>>('fleet/units', { pageSize: 500, isActive: true }).pipe(
                map((page) => ({
                  total: page.totalCount,
                  counts: STATUSES.map((status) => ({ status, count: page.items.filter((u) => u.status === status).length })),
                  workshop: page.items.filter((u) => u.status === 'workshop').slice(0, 4),
                })),
              )
            : of('forbidden' as const),
        ),
      ),
  });

  protected tone(status: string): string {
    return toneOf('unitStatus', status);
  }
}

export default FleetWidget;
