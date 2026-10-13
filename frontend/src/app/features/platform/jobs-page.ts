import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DateTimePipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { JobCompanyResult, ScheduledJobRun, ScheduledJobs } from '../../core/models';
import { apiResource } from '../../core/resource';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader } from '../../shared/ui';

/**
 * Tareas programadas (platform administrators): the reviews of every operational company that look for permits,
 * documents, licenses, contracts and price lists about to expire, maintenance due, overdue invoices and approvals
 * past their deadline. Last runs with their findings per company and "run now".
 */
@Component({
  selector: 'br-scheduled-jobs',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, DateTimePipe, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.scheduledJobs' | t" icon="schedule" [subtitle]="'jobs.hint' | t">
        <button mat-flat-button type="button" (click)="run()" [disabled]="busy()"><mat-icon>play_arrow</mat-icon>{{ 'jobs.runNow' | t }}</button>
      </br-page-header>
      @if (busy() || jobs.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (jobs.value(); as j) {
        <section class="br-card">
          <dl class="br-dl">
            <div><dt>{{ 'jobs.background' | t }}</dt><dd>{{ (j.backgroundEnabled ? 'jobs.enabled' : 'jobs.disabled') | t }}</dd></div>
            <div><dt>{{ 'jobs.interval' | t }}</dt><dd>{{ 'jobs.everyMinutes' | t: { n: j.intervalMinutes } }}</dd></div>
            <div><dt>{{ 'jobs.lastRun' | t }}</dt><dd>{{ j.runs[0] ? (j.runs[0].startedAt | dt) : '—' }}</dd></div>
            <div><dt>{{ 'jobs.nextRun' | t }}</dt><dd>{{ j.nextRunAt ? (j.nextRunAt | dt) : '—' }}</dd></div>
          </dl>
        </section>
        @if (!j.backgroundEnabled) {
          <div class="br-warning-box"><mat-icon>warning</mat-icon>{{ 'jobs.backgroundOff' | t }}</div>
        }

        <section class="br-card">
          <h2>{{ 'jobs.what' | t }}</h2>
          <div class="br-chips">
            @for (kind of kinds; track kind) {
              <span class="cl-chip">{{ 'jobs.kinds.' + kind | t }}</span>
            }
          </div>
        </section>

        <section class="br-card">
          <h2>{{ 'jobs.runs' | t }}</h2>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'jobs.startedAt' | t }}</th>
                  <th>{{ 'jobs.triggeredBy' | t }}</th>
                  <th class="br-end">{{ 'jobs.companies' | t }}</th>
                  <th class="br-end">{{ 'jobs.findings' | t }}</th>
                  <th class="br-wide">{{ 'jobs.detail' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (r of j.runs; track r.id) {
                  <tr>
                    <td class="br-nowrap">{{ r.startedAt | dt }}<div class="br-sub">{{ duration(r) }}</div></td>
                    <td>{{ r.triggeredBy === 'Programada' ? ('jobs.scheduled' | t) : r.triggeredBy }}</td>
                    <td class="br-end">{{ r.companies }}@if (r.failures > 0) {<div class="br-sub br-error-text">{{ 'jobs.failures' | t: { n: r.failures } }}</div>}</td>
                    <td class="br-end">{{ r.notices }}</td>
                    <td class="br-wide">
                      @for (d of r.details; track d.slug) {
                        @if (d.error || hasCounts(d)) {
                          <div class="br-job-company">
                            <strong>{{ d.name }}</strong>
                            @if (d.error) {
                              <span class="br-error-text">{{ d.error }}</span>
                            } @else {
                              <span class="cl-muted">{{ countsText(d) }}</span>
                            }
                          </div>
                        }
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="5" class="br-table-empty">{{ 'jobs.noRuns' | t }}</td></tr>
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
export class ScheduledJobsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);

  protected readonly kinds = [
    'documents', 'licenses', 'missingDocuments', 'contracts', 'priceLists', 'correctiveActions', 'apiKeys',
    'maintenance', 'overdueInvoices', 'uninvoicedTickets', 'lowStock', 'expiredQuotes', 'approvalReminders',
  ];

  protected readonly busy = signal(false);
  private readonly reload = signal(0);

  protected readonly jobs = apiResource({
    params: () => this.reload(),
    stream: () => this.api.get<ScheduledJobs>('platform/jobs', { take: 30 }, { tenant: false }),
  });

  protected hasCounts(result: JobCompanyResult): boolean {
    return Object.values(result.counts).some((n) => n > 0);
  }

  protected countsText(result: JobCompanyResult): string {
    return Object.entries(result.counts)
      .filter(([, n]) => n > 0)
      .map(([kind, n]) => `${this.i18n.t(`jobs.kinds.${kind}`)}: ${n}`)
      .join(' · ');
  }

  protected duration(run: ScheduledJobRun): string {
    if (!run.finishedAt) {
      return '';
    }

    const seconds = Math.max(0, Math.round((new Date(run.finishedAt).getTime() - new Date(run.startedAt).getTime()) / 1000));
    return this.i18n.t('jobs.seconds', { n: seconds });
  }

  protected async run(): Promise<void> {
    this.busy.set(true);
    try {
      const run = await firstValueFrom(this.api.post<ScheduledJobRun>('platform/jobs/run', null, { tenant: false }));
      this.dialogs.success(this.i18n.t('jobs.ran', { companies: run.companies, findings: run.notices }));
      this.reload.update((r) => r + 1);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
