import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DatePipe, DateTimePipe, MoneyPipe, NumPipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { WorkOrderAction, WorkOrderDetail } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';

/** Work order: what, where, when, with which units and crew; its tickets and the dispatch life cycle. */
@Component({
  selector: 'br-work-order-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, DatePipe, DateTimePipe, MoneyPipe, NumPipe, CanDirective, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      @if (order.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (order.value(); as o) {
        <br-page-header [title]="o.summary.folio" icon="event_note" [subtitle]="o.summary.customerName + ' · ' + o.summary.siteName">
          <a mat-button [routerLink]="nav.to('ordenes')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
          @if (open()) {
            <a *brCan="p.ticketsCapture" mat-flat-button [routerLink]="nav.to('boletas/nueva')" [queryParams]="{ orden: o.summary.id }"><mat-icon>receipt_long</mat-icon>{{ 'workOrders.captureTicket' | t }}</a>
          }
          @for (action of actions(); track action) {
            <button mat-stroked-button type="button" (click)="change(action)" [disabled]="busy()"><mat-icon>{{ icons[action] }}</mat-icon>{{ 'workOrders.actions.' + action | t }}</button>
          }
          @if (open()) {
            <a *brCan="p.dispatchManage" mat-stroked-button [routerLink]="nav.to('ordenes', o.summary.id, 'editar')"><mat-icon>edit</mat-icon>{{ 'common.edit' | t }}</a>
          }
        </br-page-header>

        @if (o.warnings.length > 0) {
          <div class="br-warning-box">
            <mat-icon>warning</mat-icon>
            <ul>
              @for (w of o.warnings; track w) {
                <li>{{ w }}</li>
              }
            </ul>
          </div>
        }

        <section class="br-card">
          <div class="br-actions">
            <br-status enum="workOrderStatus" [value]="o.summary.status" />
            <br-status enum="workOrderPriority" [value]="o.summary.priority" />
            @if (o.summary.serviceTypeName) {
              <span class="cl-chip" [style.background]="o.summary.serviceTypeColor" [style.color]="o.summary.serviceTypeColor ? '#fff' : null">{{ o.summary.serviceTypeName }}</span>
            }
          </div>
          <dl class="br-dl" style="margin-top: 12px">
            <div><dt>{{ 'fields.scheduledStart' | t }}</dt><dd>{{ o.summary.scheduledStart | dt }}</dd></div>
            <div><dt>{{ 'fields.scheduledEnd' | t }}</dt><dd>{{ o.summary.scheduledEnd | dt }}</dd></div>
            <div><dt>{{ 'fields.customer' | t }}</dt><dd>{{ o.summary.customerName }}</dd></div>
            <div><dt>{{ 'fields.contract' | t }}</dt><dd>{{ o.contractName ?? '—' }}</dd></div>
            <div><dt>{{ 'fields.site' | t }}</dt><dd>{{ o.summary.siteName }}</dd></div>
            <div><dt>{{ 'fields.customerReference' | t }}</dt><dd>{{ o.summary.customerReference ?? '—' }}</dd></div>
            <div><dt>{{ 'fields.crew' | t }}</dt><dd>{{ o.summary.crewName ?? '—' }}</dd></div>
            <div><dt>{{ 'fields.quote' | t }}</dt><dd>@if (o.quoteId) {<a [routerLink]="nav.to('cotizaciones', o.quoteId)">{{ o.quoteFolio }}</a>} @else {—}</dd></div>
          </dl>
          <h3 class="br-form-section">{{ 'fields.workToDo' | t }}</h3>
          <p class="br-pre">{{ o.summary.description }}</p>
          @if (o.instructions) {
            <h3 class="br-form-section">{{ 'fields.instructions' | t }}</h3>
            <p class="br-pre">{{ o.instructions }}</p>
          }
        </section>

        <div class="br-grid br-grid--2">
          <section class="br-card">
            <h2>{{ 'workOrders.resources' | t }}</h2>
            <div class="br-chips">
              @for (u of o.units; track u.id) {
                <a class="cl-chip cl-chip--info" [routerLink]="nav.to('flota/unidades', u.id)"><mat-icon>local_shipping</mat-icon>{{ u.code }}</a>
              }
              @for (e of o.employees; track e.id) {
                <span class="cl-chip"><mat-icon>person</mat-icon>{{ e.name }}</span>
              }
            </div>
            <h3 class="br-form-section">{{ 'workOrders.plannedServices' | t }}</h3>
            <table class="br-table">
              <tbody>
                @for (s of o.plannedServices; track s.serviceId) {
                  <tr>
                    <td>{{ s.serviceName }}</td>
                    <td class="br-end">{{ s.quantity | num }} {{ s.unitName }}</td>
                  </tr>
                } @empty {
                  <tr><td class="br-table-empty">—</td></tr>
                }
              </tbody>
            </table>
          </section>

          <section class="br-card">
            <h2>{{ 'workOrders.timeline' | t }}</h2>
            <ul class="br-timeline">
              <li><mat-icon>add_circle</mat-icon><div>{{ 'workOrders.created' | t }}<small>{{ o.createdAt | dt }} · {{ o.createdBy }}</small></div></li>
              @if (o.dispatchedAt) {
                <li><mat-icon>send</mat-icon><div>{{ 'enums.workOrderStatus.dispatched' | t }}<small>{{ o.dispatchedAt | dt }}</small></div></li>
              }
              @if (o.startedAt) {
                <li><mat-icon>play_circle</mat-icon><div>{{ 'enums.workOrderStatus.inProgress' | t }}<small>{{ o.startedAt | dt }}</small></div></li>
              }
              @if (o.completedAt) {
                <li><mat-icon>task_alt</mat-icon><div>{{ 'enums.workOrderStatus.completed' | t }}<small>{{ o.completedAt | dt }}</small></div></li>
              }
              @if (o.cancellationReason) {
                <li><mat-icon>cancel</mat-icon><div>{{ 'enums.workOrderStatus.cancelled' | t }}<small>{{ o.cancellationReason }}</small></div></li>
              }
            </ul>
          </section>
        </div>

        <section class="br-card">
          <h2>{{ 'workOrders.tickets' | t }}</h2>
          <table class="br-table">
            <thead>
              <tr>
                <th>{{ 'fields.folio' | t }}</th>
                <th>{{ 'fields.serviceDate' | t }}</th>
                <th>{{ 'fields.status' | t }}</th>
                <th class="br-end">{{ 'fields.total' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (t of o.tickets; track t.id) {
                <tr>
                  <td><a class="cl-row-link" [routerLink]="nav.to('boletas', t.id)">{{ t.folio }}</a></td>
                  <td>{{ t.serviceDate | d }}</td>
                  <td><br-status enum="ticketStatus" [value]="t.status" /></td>
                  <td class="br-end">{{ t.total | money }}</td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="br-table-empty">{{ 'workOrders.noTickets' | t }}</td></tr>
              }
            </tbody>
          </table>
        </section>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrderDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly session = inject(BrocalSession);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);
  protected readonly p = P;
  protected readonly icons: Record<WorkOrderAction, string> = { dispatch: 'send', start: 'play_circle', complete: 'task_alt', cancel: 'cancel' };

  readonly id = input.required<string>();
  protected readonly busy = signal(false);
  private readonly reload = signal(0);

  protected readonly order = apiResource({
    params: () => ({ id: this.id(), reload: this.reload() }),
    stream: ({ params }) => this.api.get<WorkOrderDetail>(`work-orders/${params.id}`),
  });

  protected readonly open = computed(() => {
    const status = this.order.value()?.summary.status;
    return status !== undefined && status !== 'completed' && status !== 'cancelled';
  });

  /** Office dispatches and cancels; the field (own orders) starts and completes. */
  protected readonly actions = computed<WorkOrderAction[]>(() => {
    const status = this.order.value()?.summary.status;
    const manage = this.session.can(P.dispatchManage);
    const field = this.session.can(P.dispatchManage, P.dispatchReadOwn, P.dispatchRead);
    const result: WorkOrderAction[] = [];
    if (status === 'scheduled' && manage) {
      result.push('dispatch');
    }

    if ((status === 'scheduled' || status === 'dispatched') && field) {
      result.push('start');
    }

    if ((status === 'dispatched' || status === 'inProgress') && field) {
      result.push('complete');
    }

    if (status !== 'completed' && status !== 'cancelled' && manage) {
      result.push('cancel');
    }

    return result;
  });

  protected async change(action: WorkOrderAction): Promise<void> {
    let reason: string | null = null;
    if (action === 'cancel') {
      reason = await this.dialogs.prompt(this.i18n.t('workOrders.cancelTitle'), this.i18n.t('fields.reason'));
      if (!reason) {
        return;
      }
    }

    this.busy.set(true);
    try {
      await firstValueFrom(this.api.post(`work-orders/${this.id()}/status`, { action, reason }));
      this.dialogs.success(this.i18n.t('common.saved'));
      this.reload.update((r) => r + 1);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
