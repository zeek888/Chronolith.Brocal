import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { fileUrl } from '../../core/config';
import { DateTimePipe, NumPipe, firstOfMonth, today } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { CheckResult, HseStats, InspectionDetail, InspectionSummary, InspectionTemplate, Lookup } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { Chart, ChartSpec } from '../../shared/chart';
import { Dialogs } from '../../shared/dialogs';
import { LookupSelect } from '../../shared/lookup-select';
import { KpiCard, PageHeader, StatusChip } from '../../shared/ui';

interface AnswerState {
  key: string;
  question: string;
  isCritical: boolean;
  result: CheckResult | null;
  comment: string;
}

/** Checklist inspection (pre-use of a unit, PPE, site): OK / not OK / N/A per question; failed critical items are findings. */
@Component({
  selector: 'br-inspection-record',
  imports: [FormsModule, ReactiveFormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, TPipe, LookupSelect, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'hse.newInspection' | t" icon="fact_check" [subtitle]="'hse.inspectionHint' | t">
        <a mat-button [routerLink]="nav.to('hse/inspecciones')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
      </br-page-header>
      <section class="br-card">
        <div class="cl-form-grid">
          <br-lookup [formControl]="templateId" catalog="hse/templates" [label]="'hse.template' | t" [required]="true" (selected)="templateSelected($event)" />
          <br-lookup [formControl]="unitId" catalog="fleet/units" [label]="'fields.unit' | t" />
          <br-lookup [formControl]="siteId" catalog="catalogs/sites" [label]="'fields.site' | t" />
        </div>
      </section>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (answers().length > 0) {
        <section class="br-card">
          <div class="br-card__header">
            <h2>{{ templateName() }}</h2>
            <button mat-stroked-button type="button" (click)="allOk()"><mat-icon>done_all</mat-icon>{{ 'hse.allOk' | t }}</button>
          </div>
          <div class="br-checklist">
            @for (answer of answers(); track answer.key; let i = $index) {
              <div class="br-check" [class.br-check--fail]="answer.result === 'notOk'">
                <div class="br-check__question">
                  @if (answer.isCritical) {
                    <mat-icon class="br-check__critical">priority_high</mat-icon>
                  }
                  {{ answer.question }}
                </div>
                <mat-button-toggle-group [ngModel]="answer.result" (ngModelChange)="set(i, { result: $event })">
                  <mat-button-toggle value="ok"><mat-icon>check</mat-icon></mat-button-toggle>
                  <mat-button-toggle value="notOk"><mat-icon>close</mat-icon></mat-button-toggle>
                  <mat-button-toggle value="notApplicable">N/A</mat-button-toggle>
                </mat-button-toggle-group>
                @if (answer.result === 'notOk') {
                  <input class="br-check__comment" [placeholder]="'hse.findingComment' | t" [ngModel]="answer.comment" (ngModelChange)="set(i, { comment: $event })" />
                }
              </div>
            }
          </div>
          <mat-form-field class="br-full" style="margin-top: 12px">
            <mat-label>{{ 'fields.notes' | t }}</mat-label>
            <textarea matInput rows="2" [formControl]="notes"></textarea>
          </mat-form-field>
          <div class="br-actions">
            <span class="cl-muted">{{ 'hse.answered' | t: { n: answered(), total: answers().length } }}</span>
            <span class="cl-spacer"></span>
            <button mat-flat-button type="button" (click)="save()" [disabled]="saving() || answered() < answers().length"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .br-checklist { display: flex; flex-direction: column; gap: 8px; }
    .br-check { display: grid; grid-template-columns: 1fr auto; gap: 8px 16px; align-items: center; padding: 10px 12px; border-radius: var(--cl-radius-sm, 8px); background: var(--mat-sys-surface-container-low); }
    .br-check--fail { background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container); }
    .br-check__question { display: flex; align-items: center; gap: 6px; }
    .br-check__critical { color: var(--mat-sys-error); }
    .br-check__comment { grid-column: 1 / -1; padding: 8px; border-radius: 6px; border: 1px solid var(--mat-sys-outline-variant); background: var(--mat-sys-surface-container-lowest); color: var(--mat-sys-on-surface); }
    @media (max-width: 600px) { .br-check { grid-template-columns: 1fr; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InspectionRecordPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly nav = inject(BrocalNav);

  protected readonly templateId = new FormControl<string | null>(null);
  protected readonly unitId = new FormControl<string | null>(null);
  protected readonly siteId = new FormControl<string | null>(null);
  protected readonly notes = new FormControl<string | null>(null);
  protected readonly answers = signal<AnswerState[]>([]);
  protected readonly templateName = signal('');
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly answered = computed(() => this.answers().filter((a) => a.result !== null).length);

  protected async templateSelected(selected: Lookup | Lookup[] | null): Promise<void> {
    const id = Array.isArray(selected) ? null : selected?.id;
    if (!id) {
      this.answers.set([]);
      return;
    }

    this.loading.set(true);
    try {
      const template = await firstValueFrom(this.api.get<InspectionTemplate>(`hse/templates/${id}`));
      this.templateName.set(template.name);
      this.answers.set(template.items.map((item) => ({ key: item.key, question: item.question, isCritical: item.isCritical, result: null, comment: '' })));
    } finally {
      this.loading.set(false);
    }
  }

  protected set(index: number, change: Partial<AnswerState>): void {
    this.answers.update((list) => list.map((a, i) => (i === index ? { ...a, ...change } : a)));
  }

  protected allOk(): void {
    this.answers.update((list) => list.map((a) => (a.result ? a : { ...a, result: 'ok' })));
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    try {
      const result = await firstValueFrom(
        this.api.post<InspectionSummary>('hse/inspections', {
          templateId: this.templateId.value,
          performedAt: new Date().toISOString(),
          unitId: this.unitId.value,
          siteId: this.siteId.value,
          workOrderId: null,
          answers: this.answers().map((a) => ({ key: a.key, result: a.result, comment: a.comment || null, photoFileId: null })),
          notes: this.notes.value,
        }),
      );
      this.dialogs.success(this.i18n.t(result.passed ? 'hse.inspectionPassed' : 'hse.inspectionFailed', { n: result.findings }));
      await this.nav.navigate(['hse/inspecciones', result.id]);
    } catch {
      this.saving.set(false);
    }
  }
}

@Component({
  selector: 'br-inspection-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, DateTimePipe, NumPipe, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      @if (inspection.value(); as i) {
        <br-page-header [title]="i.summary.templateName" icon="fact_check" [subtitle]="(i.summary.performedAt | dt) + ' · ' + i.summary.inspectorName">
          <a mat-button [routerLink]="nav.to('hse/inspecciones')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
        </br-page-header>
        <section class="br-card">
          <div class="br-actions">
            <span class="cl-chip" [class.cl-chip--success]="i.summary.passed" [class.cl-chip--error]="!i.summary.passed">{{ (i.summary.passed ? 'hse.passed' : 'hse.notPassed') | t }}</span>
            <span class="cl-chip">{{ 'hse.score' | t }}: {{ i.summary.score | num: 0 }} %</span>
            <span class="cl-chip">{{ 'hse.findings' | t }}: {{ i.summary.findings }} ({{ i.summary.criticalFindings }} {{ 'hse.critical' | t }})</span>
            @if (i.summary.unitCode) {
              <span class="cl-chip cl-chip--info">{{ i.summary.unitCode }}</span>
            }
            @if (i.summary.siteName) {
              <span class="cl-chip">{{ i.summary.siteName }}</span>
            }
          </div>
          <table class="br-table" style="margin-top: 12px">
            <tbody>
              @for (a of i.answers; track a.key) {
                <tr>
                  <td>{{ a.question }}@if (a.isCritical) {<strong> *</strong>}@if (a.comment) {<div class="br-sub">{{ a.comment }}</div>}</td>
                  <td class="br-end"><br-status enum="checkResult" [value]="a.result" /></td>
                </tr>
              }
            </tbody>
          </table>
          @if (i.notes) {
            <p class="br-pre">{{ i.notes }}</p>
          }
        </section>
      } @else if (inspection.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InspectionDetailPage {
  private readonly api = inject(BrocalApi);
  protected readonly nav = inject(BrocalNav);
  readonly id = input.required<string>();
  protected readonly inspection = apiResource({ params: () => this.id(), stream: ({ params }) => this.api.get<InspectionDetail>(`hse/inspections/${params}`) });
  protected url(relative: string): string {
    return fileUrl(relative) ?? '';
  }
}

/** Indicadores HSE: incidents by type, lost days, inspections and talks of the period. */
@Component({
  selector: 'br-hse-stats',
  imports: [FormsModule, MatDatepickerModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, TPipe, Chart, KpiCard, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.hseStats' | t" icon="monitor_heart" [subtitle]="'hse.statsHint' | t" />
      <div class="cl-toolbar-row br-filters">
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.from' | t }}</mat-label>
          <input matInput [matDatepicker]="from" [ngModel]="fromDate()" (ngModelChange)="fromDate.set($event)" />
          <mat-datepicker-toggle matIconSuffix [for]="from" />
          <mat-datepicker #from />
        </mat-form-field>
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.to' | t }}</mat-label>
          <input matInput [matDatepicker]="to" [ngModel]="toDate()" (ngModelChange)="toDate.set($event)" />
          <mat-datepicker-toggle matIconSuffix [for]="to" />
          <mat-datepicker #to />
        </mat-form-field>
      </div>
      @if (stats.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (stats.value(); as s) {
        <div class="br-kpis">
          <br-kpi [label]="'hse.daysWithoutLta' | t" [value]="s.daysWithoutLostTimeAccident" icon="shield" tone="success" />
          <br-kpi [label]="'hse.incidents' | t" [value]="s.incidents" icon="report" [detail]="'hse.openCount' | t: { n: s.openIncidents }" />
          <br-kpi [label]="'hse.accidents' | t" [value]="s.accidents" icon="personal_injury" [tone]="s.accidents > 0 ? 'error' : 'neutral'" />
          <br-kpi [label]="'hse.nearMisses' | t" [value]="s.nearMisses" icon="warning" tone="warning" />
          <br-kpi [label]="'hse.lostDays' | t" [value]="s.lostDays" icon="event_busy" />
          <br-kpi [label]="'hse.openActions' | t" [value]="s.openActions" icon="pending_actions" />
          <br-kpi [label]="'nav.inspections' | t" [value]="s.inspections" icon="fact_check" [detail]="'hse.averageScore' | t: { n: s.averageScore }" />
          <br-kpi [label]="'nav.safetyTalks' | t" [value]="s.safetyTalks" icon="record_voice_over" [detail]="'hse.attendeesCount' | t: { n: s.talkAttendees }" />
        </div>
        <section class="br-card">
          <h2>{{ 'hse.byType' | t }}</h2>
          <br-chart [spec]="chart()" />
        </section>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HseStatsPage {
  private readonly api = inject(BrocalApi);
  private readonly i18n = inject(I18n);
  protected readonly fromDate = signal(firstOfMonth(-11));
  protected readonly toDate = signal(today());
  protected readonly stats = apiResource({
    params: () => ({ from: this.fromDate(), to: this.toDate() }),
    stream: ({ params }) => this.api.get<HseStats>('hse/stats', params),
  });
  protected readonly chart = computed<ChartSpec>(() => ({
    kind: 'pie',
    items: (this.stats.value()?.byType ?? []).map((k) => ({ name: this.i18n.enum('incidentType', k.label) || k.label, value: k.value })),
  }));
}
