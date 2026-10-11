import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { BrocalApi } from '../../core/api';
import { activeLocale } from '../../core/i18n';
import { TPipe } from '../../core/i18n';
import { DispatchBoard, WorkOrderSummary } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { CanDirective } from '../../core/session';
import { PageHeader, StatusChip, toneOf } from '../../shared/ui';

const DAY = 86_400_000;

interface Bar {
  order: WorkOrderSummary;
  start: number;
  span: number;
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/**
 * Programación (dispatch): which unit goes to which well and when. Units are rows, days are columns and
 * each order is a bar over its scheduled days; overlapping orders of a unit are a scheduling conflict.
 */
@Component({
  selector: 'br-dispatch-board',
  imports: [RouterLink, MatButtonModule, MatButtonToggleModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TPipe, CanDirective, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.dispatchBoard' | t" icon="view_timeline" [subtitle]="'dispatch.hint' | t">
        <a mat-button [routerLink]="nav.to('ordenes')"><mat-icon>list</mat-icon>{{ 'nav.workOrders' | t }}</a>
        <a *brCan="p.dispatchManage" mat-flat-button [routerLink]="nav.to('ordenes/nueva')"><mat-icon>add</mat-icon>{{ 'workOrders.new' | t }}</a>
      </br-page-header>

      <div class="cl-toolbar-row">
        <button mat-icon-button type="button" (click)="move(-1)" [attr.aria-label]="'common.previous' | t"><mat-icon>chevron_left</mat-icon></button>
        <button mat-stroked-button type="button" (click)="goToday()">{{ 'common.today' | t }}</button>
        <button mat-icon-button type="button" (click)="move(1)" [attr.aria-label]="'common.next' | t"><mat-icon>chevron_right</mat-icon></button>
        <strong>{{ rangeLabel() }}</strong>
        <span class="cl-spacer"></span>
        <mat-button-toggle-group [value]="days()" (change)="days.set($event.value)">
          <mat-button-toggle [value]="7">{{ 'dispatch.week' | t }}</mat-button-toggle>
          <mat-button-toggle [value]="14">{{ 'dispatch.twoWeeks' | t }}</mat-button-toggle>
        </mat-button-toggle-group>
      </div>

      @if (board.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      <div class="br-board cl-surface" [style.--br-days]="days()">
        <div class="br-board__head">
          <div class="br-board__unit">{{ 'fields.unit' | t }}</div>
          @for (day of dayLabels(); track day.key) {
            <div class="br-board__day" [class.br-board__day--today]="day.today" [class.br-board__day--weekend]="day.weekend">{{ day.label }}</div>
          }
        </div>
        @for (row of rows(); track row.unit.id) {
          <div class="br-board__row">
            <div class="br-board__unit">
              <a [routerLink]="nav.to('flota/unidades', row.unit.id)"><strong>{{ row.unit.code }}</strong></a>
              <small>{{ row.unit.name }}</small>
              <br-status enum="unitStatus" [value]="row.unit.status" />
            </div>
            <div class="br-board__lane" [style.grid-template-rows]="'repeat(' + row.lanes + ', 34px)'">
              @for (bar of row.bars; track bar.order.id + '-' + $index) {
                <a
                  class="br-board__bar"
                  [class]="'br-board__bar br-board__bar--' + tone(bar.order.status)"
                  [style.grid-column]="bar.start + 1 + ' / span ' + bar.span"
                  [style.grid-row]="bar.lane + 1"
                  [style.border-left-color]="bar.order.serviceTypeColor ?? null"
                  [routerLink]="nav.to('ordenes', bar.order.id)"
                  [matTooltip]="bar.order.folio + ' · ' + bar.order.customerName + ' · ' + bar.order.siteName + ' · ' + bar.order.description"
                >
                  <strong>{{ bar.order.folio }}</strong> {{ bar.order.siteName }}
                </a>
              }
            </div>
          </div>
        } @empty {
          <div class="br-board__empty cl-muted">{{ 'dispatch.noUnits' | t }}</div>
        }
        @if (unassigned().length > 0) {
          <div class="br-board__row">
            <div class="br-board__unit"><strong>{{ 'dispatch.unassigned' | t }}</strong></div>
            <div class="br-board__lane" [style.grid-template-rows]="'repeat(' + unassigned().length + ', 34px)'">
              @for (bar of unassigned(); track bar.order.id; let i = $index) {
                <a class="br-board__bar br-board__bar--neutral" [style.grid-column]="bar.start + 1 + ' / span ' + bar.span" [style.grid-row]="i + 1" [routerLink]="nav.to('ordenes', bar.order.id)">
                  <strong>{{ bar.order.folio }}</strong> {{ bar.order.siteName }}
                </a>
              }
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .br-board { overflow-x: auto; }
    .br-board__head, .br-board__row { display: grid; grid-template-columns: 180px repeat(var(--br-days), minmax(72px, 1fr)); min-width: calc(180px + var(--br-days) * 72px); }
    .br-board__row { border-top: 1px solid var(--mat-sys-outline-variant); }
    .br-board__head { position: sticky; top: 0; background: var(--mat-sys-surface-container-low); z-index: 1; }
    .br-board__unit { display: flex; flex-direction: column; gap: 2px; padding: 8px 10px; border-right: 1px solid var(--mat-sys-outline-variant); font: var(--mat-sys-body-small); }
    .br-board__unit a { color: inherit; text-decoration: none; }
    .br-board__day { padding: 8px 4px; text-align: center; font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); border-right: 1px solid var(--mat-sys-outline-variant); }
    .br-board__day--weekend { background: var(--mat-sys-surface-container); }
    .br-board__day--today { color: var(--mat-sys-primary); font-weight: 700; }
    .br-board__lane { grid-column: 2 / -1; display: grid; grid-template-columns: repeat(var(--br-days), minmax(72px, 1fr)); gap: 4px 0; padding: 6px 0; }
    .br-board__bar { margin: 0 3px; padding: 6px 8px; border-radius: 8px; border-left: 4px solid var(--mat-sys-primary); overflow: hidden; white-space: nowrap; text-overflow: ellipsis; text-decoration: none; font: var(--mat-sys-body-small); background: var(--mat-sys-surface-container-high); color: var(--mat-sys-on-surface); }
    .br-board__bar--info { background: var(--cl-info-container); color: var(--cl-on-info-container); }
    .br-board__bar--warning { background: var(--cl-warning-container); color: var(--cl-on-warning-container); }
    .br-board__bar--success { background: var(--cl-success-container); color: var(--cl-on-success-container); }
    .br-board__bar--error { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); text-decoration: line-through; }
    .br-board__empty { padding: 24px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DispatchBoardPage {
  private readonly api = inject(BrocalApi);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;

  protected readonly days = signal(7);
  protected readonly from = signal(startOfDay(new Date(Date.now() - 2 * DAY)));
  private readonly to = computed(() => new Date(this.from().getTime() + this.days() * DAY));

  protected readonly board = apiResource({
    params: () => ({ from: this.from().toISOString(), to: this.to().toISOString() }),
    stream: ({ params }) => this.api.get<DispatchBoard>('work-orders/board', params),
  });

  protected readonly dayLabels = computed(() => {
    const locale = activeLocale();
    const today = startOfDay(new Date()).getTime();
    return Array.from({ length: this.days() }, (_, i) => {
      const date = new Date(this.from().getTime() + i * DAY);
      return {
        key: date.toISOString(),
        label: new Intl.DateTimeFormat(locale, { weekday: 'short', day: '2-digit' }).format(date),
        today: date.getTime() === today,
        weekend: date.getDay() === 0 || date.getDay() === 6,
      };
    });
  });

  protected readonly rangeLabel = computed(() => {
    const format = new Intl.DateTimeFormat(activeLocale(), { day: '2-digit', month: 'short', year: 'numeric' });
    return `${format.format(this.from())} – ${format.format(new Date(this.to().getTime() - DAY))}`;
  });

  private bar(order: WorkOrderSummary): Bar | null {
    const from = this.from().getTime();
    const start = Math.max(0, Math.floor((startOfDay(new Date(order.scheduledStart)).getTime() - from) / DAY));
    const end = Math.min(this.days() - 1, Math.floor((startOfDay(new Date(order.scheduledEnd)).getTime() - from) / DAY));
    return end < 0 || start > this.days() - 1 || end < start ? null : { order, start, span: end - start + 1 };
  }

  protected readonly rows = computed(() => {
    const board = this.board.value();
    if (!board) {
      return [];
    }

    return board.units.map((unit) => {
      const bars = board.orders
        .filter((o) => o.unitIds.includes(unit.id))
        .map((o) => this.bar(o))
        .filter((b): b is Bar => b !== null)
        .sort((a, b) => a.start - b.start);
      // Lanes: overlapping orders of the same unit go to separate rows (and stand out as conflicts).
      const laneEnds: number[] = [];
      const placed = bars.map((bar) => {
        let lane = laneEnds.findIndex((end) => end < bar.start);
        if (lane < 0) {
          lane = laneEnds.length;
          laneEnds.push(-1);
        }

        laneEnds[lane] = bar.start + bar.span - 1;
        return { ...bar, lane };
      });
      return { unit, bars: placed, lanes: Math.max(1, laneEnds.length) };
    });
  });

  protected readonly unassigned = computed(() =>
    (this.board.value()?.orders ?? [])
      .filter((o) => o.unitIds.length === 0)
      .map((o) => this.bar(o))
      .filter((b): b is Bar => b !== null),
  );

  protected move(direction: number): void {
    this.from.set(new Date(this.from().getTime() + direction * this.days() * DAY));
  }

  protected goToday(): void {
    this.from.set(startOfDay(new Date(Date.now() - 2 * DAY)));
  }

  protected tone(status: string): string {
    return toneOf('workOrderStatus', status);
  }
}
