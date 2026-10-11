import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NumPipe, MoneyPipe, money, num } from '../core/format';
import { EnumPipe, I18n } from '../core/i18n';

/** Page title with the brand underline, like the pages of the ChronoLith Shell. */
@Component({
  selector: 'br-page-header',
  imports: [MatIconModule],
  template: `
    <header class="br-page-header">
      @if (icon(); as icon) {
        <span class="br-page-header__icon"><mat-icon>{{ icon }}</mat-icon></span>
      }
      <div class="br-page-header__text">
        <h1>{{ title() }}</h1>
        @if (subtitle(); as subtitle) {
          <p class="cl-muted">{{ subtitle }}</p>
        }
        <div class="cl-brand-rule"></div>
      </div>
      <div class="br-page-header__actions">
        <ng-content />
      </div>
    </header>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
  readonly icon = input<string | null>(null);
}

@Component({
  selector: 'br-empty',
  imports: [MatIconModule],
  template: `
    <div class="br-empty">
      <mat-icon class="br-empty__icon">{{ icon() }}</mat-icon>
      <h2>{{ title() }}</h2>
      @if (message(); as message) {
        <p class="cl-muted">{{ message }}</p>
      }
      <ng-content />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly icon = input('inbox');
  readonly title = input.required<string>();
  readonly message = input<string | null>(null);
}

export type Tone = 'success' | 'warning' | 'info' | 'error' | 'primary' | 'neutral';

/** Colors of the status of each kind of record. */
const TONES: Record<string, Record<string, Tone>> = {
  ticketStatus: { draft: 'neutral', signed: 'info', approved: 'primary', invoiced: 'success', cancelled: 'error' },
  workOrderStatus: { scheduled: 'neutral', dispatched: 'info', inProgress: 'warning', completed: 'success', cancelled: 'error' },
  workOrderPriority: { low: 'neutral', normal: 'info', high: 'warning', urgent: 'error' },
  unitStatus: { available: 'success', operating: 'info', workshop: 'warning', outOfService: 'error' },
  complianceStatus: { noExpiry: 'neutral', valid: 'success', expiringSoon: 'warning', expired: 'error', missing: 'error' },
  maintenanceOrderStatus: { open: 'info', inProgress: 'warning', waitingParts: 'warning', closed: 'success', cancelled: 'neutral' },
  invoiceStatus: { draft: 'neutral', stamped: 'success', cancellationPending: 'warning', cancelled: 'error' },
  tripStatus: { draft: 'neutral', stamped: 'success', cancelled: 'error' },
  estimateStatus: { draft: 'neutral', submitted: 'info', approved: 'primary', copadeAssigned: 'primary', invoiced: 'success', paid: 'success', rejected: 'error' },
  quoteStatus: { draft: 'neutral', sent: 'info', accepted: 'success', rejected: 'error', expired: 'warning', converted: 'primary' },
  expenseStatus: { draft: 'neutral', submitted: 'info', approved: 'primary', rejected: 'error', paid: 'success' },
  incidentStatus: { reported: 'warning', investigating: 'info', closed: 'success' },
  incidentSeverity: { low: 'neutral', medium: 'info', high: 'warning', critical: 'error' },
  tenantStatus: { trial: 'info', active: 'success', suspended: 'warning', cancelled: 'error' },
  membershipStatus: { invited: 'info', active: 'success', disabled: 'neutral' },
  outboxStatus: { pending: 'info', sent: 'success', failed: 'error', simulated: 'neutral' },
  paymentComplementStatus: { notRequired: 'neutral', pending: 'warning', stamped: 'success' },
  actionStatus: { pending: 'warning', inProgress: 'info', done: 'success' },
  checkResult: { ok: 'success', notOk: 'error', notApplicable: 'neutral' },
};

export function toneOf(enumName: string, value: string | null | undefined): Tone {
  return (value && TONES[enumName]?.[value]) || 'neutral';
}

/** Status chip with the translated label: `<br-status enum="ticketStatus" [value]="t.status" />`. */
@Component({
  selector: 'br-status',
  imports: [EnumPipe],
  template: `<span class="cl-chip" [class]="'cl-chip cl-chip--' + tone()">{{ value() | enum: enum() }}</span>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusChip {
  readonly enum = input.required<string>();
  readonly value = input<string | null | undefined>(null);
  protected readonly tone = computed(() => toneOf(this.enum(), this.value()));
}

/** Key figure of a dashboard or summary. */
@Component({
  selector: 'br-kpi',
  imports: [MatIconModule, MatTooltipModule],
  template: `
    <div class="br-kpi" [class]="'br-kpi br-kpi--' + tone()" [matTooltip]="hint() ?? ''">
      @if (icon(); as icon) {
        <mat-icon>{{ icon }}</mat-icon>
      }
      <div class="br-kpi__body">
        <span class="br-kpi__label">{{ label() }}</span>
        <strong class="br-kpi__value" [attr.title]="text()">{{ text() }}</strong>
        @if (detail(); as detail) {
          <span class="br-kpi__detail">{{ detail }}</span>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiCard {
  private readonly i18n = inject(I18n);

  readonly label = input.required<string>();
  readonly value = input<number | string | null>(null);
  readonly format = input<'money' | 'number' | 'percent' | 'days' | 'text'>('number');
  readonly icon = input<string | null>(null);
  readonly detail = input<string | null>(null);
  readonly hint = input<string | null>(null);
  readonly tone = input<Tone>('neutral');

  protected readonly text = computed(() => {
    const value = this.value();
    if (value === null || value === undefined) {
      return '—';
    }

    if (typeof value === 'string') {
      return value;
    }

    switch (this.format()) {
      case 'money':
        return money(value);
      case 'percent':
        return `${num(value, 1)} %`;
      case 'days':
        return this.i18n.t('common.daysValue', { n: num(value, 1) });
      default:
        return num(value, 2);
    }
  });
}

export const SHARED_PIPES = [MoneyPipe, NumPipe] as const;
