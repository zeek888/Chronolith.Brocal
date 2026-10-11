import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { DateTimePipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { BrocalNav } from '../../core/nav';
import { TicketOutbox } from '../../core/offline/ticket-outbox';
import { Dialogs } from '../../shared/dialogs';
import { EmptyState, PageHeader } from '../../shared/ui';

/** Captures waiting on this device for signal: sent automatically when the connection returns. */
@Component({
  selector: 'br-pending-tickets',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, DateTimePipe, EmptyState, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'tickets.pending' | t" icon="cloud_upload" [subtitle]="'tickets.pendingHint' | t">
        <a mat-button [routerLink]="nav.to('boletas')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
        <button mat-flat-button type="button" (click)="sync()" [disabled]="outbox.syncing() || !outbox.online() || outbox.count() === 0">
          <mat-icon>sync</mat-icon>{{ 'tickets.syncNow' | t }}
        </button>
      </br-page-header>

      @if (!outbox.online()) {
        <div class="br-warning-box"><mat-icon>cloud_off</mat-icon>{{ 'tickets.offlineNow' | t }}</div>
      }
      @if (outbox.syncing()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @for (item of outbox.pending(); track item.clientId) {
        <section class="br-card br-pending">
          <mat-icon>{{ item.lastError ? 'error' : item.signature ? 'draw' : 'receipt_long' }}</mat-icon>
          <div class="br-pending__text">
            <strong>{{ item.folio ?? ('tickets.notSentYet' | t) }} · {{ item.label }}</strong>
            <small class="cl-muted">
              {{ 'tickets.capturedOn' | t: { date: (item.createdAt | dt) } }} · {{ 'tickets.photosAndSignature' | t: { n: item.photos.length, signed: item.signature ? 1 : 0 } }}
            </small>
            @if (item.lastError) {
              <small class="br-pending__error">{{ item.lastError }}</small>
            }
          </div>
          <button mat-button type="button" (click)="discard(item.clientId)"><mat-icon>delete</mat-icon>{{ 'common.discard' | t }}</button>
        </section>
      } @empty {
        <br-empty icon="cloud_done" [title]="'tickets.noPending' | t" />
      }
    </div>
  `,
  styles: `
    .br-pending { display: flex; align-items: center; gap: 12px; }
    .br-pending__text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .br-pending__error { color: var(--mat-sys-error); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PendingTicketsPage {
  protected readonly outbox = inject(TicketOutbox);
  protected readonly nav = inject(BrocalNav);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);

  protected async sync(): Promise<void> {
    const sent = await this.outbox.sync();
    if (sent > 0) {
      this.dialogs.success(this.i18n.t('tickets.syncedCount', { n: sent }));
    }
  }

  protected async discard(clientId: string): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('tickets.discardTitle'), this.i18n.t('tickets.discardMessage'), this.i18n.t('common.discard'), true)) {
      await this.outbox.discard(clientId);
    }
  }
}
