import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DateTimePipe, dateTime } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { CatalogPush, CatalogSyncRunSummary, SharedCatalogStatus } from '../../core/models';
import { apiResource } from '../../core/resource';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';

/**
 * Catálogos de ChronoLith (platform administrators): state of the local replica of the shared catalogs of the
 * Shell (cursor of the change feed, last poll and report, elements per catalog, last error), the pushes to the
 * master waiting for the service account or rejected by it, and the buttons to poll the feed now or reload everything.
 */
@Component({
  selector: 'br-shared-catalogs',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TPipe, DateTimePipe, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.sharedCatalogs' | t" icon="sync_alt" [subtitle]="'sharedCatalogs.hint' | t">
        <button mat-stroked-button type="button" (click)="run(true)" [disabled]="busy() || !available()"><mat-icon>cloud_download</mat-icon>{{ 'sharedCatalogs.reload' | t }}</button>
        <button mat-flat-button type="button" (click)="run(false)" [disabled]="busy() || !available()"><mat-icon>sync</mat-icon>{{ 'sharedCatalogs.syncNow' | t }}</button>
      </br-page-header>
      @if (busy() || status.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (status.value(); as s) {
        @if (!s.available) {
          <div class="br-warning-box"><mat-icon>link_off</mat-icon>{{ 'sharedCatalogs.notAvailable' | t }}</div>
        } @else if (!s.backgroundEnabled) {
          <div class="br-warning-box"><mat-icon>warning</mat-icon>{{ 'sharedCatalogs.backgroundOff' | t }}</div>
        }
        @if (s.lastError) {
          <div class="br-warning-box br-warning-box--error"><mat-icon>error</mat-icon>{{ 'sharedCatalogs.lastError' | t: { at: s.lastErrorAt ? dateTime(s.lastErrorAt) : '—' } }}: {{ s.lastError }}</div>
        }

        <section class="br-card">
          <h2>{{ 'sharedCatalogs.feed' | t }}</h2>
          <dl class="br-dl">
            <div><dt>{{ 'sharedCatalogs.cursor' | t }}</dt><dd>{{ s.cursor }}@if (s.latest > s.cursor) {<span class="br-sub">{{ 'sharedCatalogs.lag' | t: { n: s.latest - s.cursor } }}</span>}</dd></div>
            <div><dt>{{ 'sharedCatalogs.latest' | t }}</dt><dd>{{ s.latest }}</dd></div>
            <div><dt>{{ 'sharedCatalogs.lastPolledAt' | t }}</dt><dd>{{ s.lastPolledAt | dt }}</dd></div>
            <div><dt>{{ 'sharedCatalogs.lastReportedAt' | t }}</dt><dd>{{ s.lastReportedAt | dt }}</dd></div>
            <div><dt>{{ 'sharedCatalogs.lastFullSyncAt' | t }}</dt><dd>{{ s.lastFullSyncAt | dt }}</dd></div>
            <div><dt>{{ 'sharedCatalogs.interval' | t }}</dt><dd>{{ 'jobs.everyMinutes' | t: { n: s.intervalMinutes } }}</dd></div>
            <div><dt>{{ 'sharedCatalogs.lastRun' | t }}</dt><dd>{{ 'sharedCatalogs.lastRunValue' | t: { applied: s.lastRunApplied, errors: s.lastRunErrors } }}</dd></div>
          </dl>
        </section>

        <section class="br-card">
          <h2>{{ 'sharedCatalogs.replica' | t }}</h2>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'sharedCatalogs.catalog' | t }}</th>
                  <th class="br-end">{{ 'sharedCatalogs.active' | t }}</th>
                  <th class="br-end">{{ 'sharedCatalogs.total' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (c of s.counts; track c.catalog) {
                  <tr>
                    <td>{{ 'enums.sharedCatalog.' + c.catalog | t }}</td>
                    <td class="br-end">{{ c.active }}</td>
                    <td class="br-end">{{ c.total }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>

        <section class="br-card">
          <h2>{{ 'sharedCatalogs.pushes' | t }}</h2>
          <p class="cl-muted">{{ 'sharedCatalogs.pushesHint' | t: { pending: s.pendingPushes, rejected: s.rejectedPushes, failed: s.failedPushes } }}</p>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'sharedCatalogs.record' | t }}</th>
                  <th>{{ 'sharedCatalogs.company' | t }}</th>
                  <th>{{ 'sharedCatalogs.operation' | t }}</th>
                  <th>{{ 'common.status' | t }}</th>
                  <th class="br-wide">{{ 'sharedCatalogs.detail' | t }}</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (p of s.pushes; track p.id) {
                  <tr>
                    <td>{{ p.summary }}<div class="br-sub">{{ 'enums.pushEntity.' + p.entityType | t }}</div></td>
                    <td>{{ p.tenantName }}</td>
                    <td class="br-nowrap">{{ p.method }} · {{ 'enums.sharedCatalog.' + p.catalog | t }}<div class="br-sub">{{ p.createdAt | dt }} · {{ 'sharedCatalogs.attempts' | t: { n: p.attempts } }}</div></td>
                    <td><br-status enum="catalogPushStatus" [value]="p.status" /></td>
                    <td class="br-wide">@if (p.lastError) {<span class="br-error-text">{{ p.lastError }}</span>} @else if (p.status === 'pending') {<span class="cl-muted">{{ 'sharedCatalogs.nextAttempt' | t: { at: dateTime(p.nextAttemptAt) } }}</span>}</td>
                    <td class="br-nowrap">
                      @if (p.status !== 'pending') {
                        <button mat-icon-button type="button" (click)="retry(p)" [matTooltip]="'common.retry' | t"><mat-icon>replay</mat-icon></button>
                      }
                      <button mat-icon-button type="button" (click)="discard(p)" [matTooltip]="'common.discard' | t"><mat-icon>delete</mat-icon></button>
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="6" class="br-table-empty">{{ 'sharedCatalogs.noPushes' | t }}</td></tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SharedCatalogsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);

  protected readonly busy = signal(false);
  private readonly reload = signal(0);

  protected readonly status = apiResource({
    params: () => this.reload(),
    stream: () => this.api.get<SharedCatalogStatus>('platform/shared-catalogs', undefined, { tenant: false }),
  });

  protected readonly available = computed(() => this.status.value()?.available ?? false);

  protected dateTime(value: string): string {
    return dateTime(value);
  }

  protected async run(full: boolean): Promise<void> {
    if (full && !(await this.dialogs.confirm(this.i18n.t('sharedCatalogs.reload'), this.i18n.t('sharedCatalogs.reloadConfirm'), this.i18n.t('sharedCatalogs.reload')))) {
      return;
    }

    this.busy.set(true);
    try {
      const summary = await firstValueFrom(this.api.post<CatalogSyncRunSummary>(full ? 'platform/shared-catalogs/reload' : 'platform/shared-catalogs/sync', null, { tenant: false }));
      this.dialogs.success(this.i18n.t(summary.fullSync ? 'sharedCatalogs.reloaded' : 'sharedCatalogs.synced', { applied: summary.applied, errors: summary.errors, pushed: summary.pushed, cursor: summary.cursor }));
      this.reload.update((r) => r + 1);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }

  protected async retry(push: CatalogPush): Promise<void> {
    await firstValueFrom(this.api.post(`platform/shared-catalogs/pushes/${push.id}/retry`, null, { tenant: false }));
    this.reload.update((r) => r + 1);
  }

  protected async discard(push: CatalogPush): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('common.discard'), this.i18n.t('sharedCatalogs.discardConfirm', { name: push.summary }), this.i18n.t('common.discard'), true)) {
      await firstValueFrom(this.api.delete(`platform/shared-catalogs/pushes/${push.id}`, undefined, { tenant: false }));
      this.reload.update((r) => r + 1);
    }
  }
}
