import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { catchError, firstValueFrom, of } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { AutomationCatalogs } from '../../core/automation';
import { DateTimePipe, MoneyPipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { ApprovalDetail, ApprovalSummary, ApprovalView, AutomationDocument, PagedResult, WorkflowStatus } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { InboxService } from '../../core/notifications';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';

const STATUSES: WorkflowStatus[] = ['pending', 'approved', 'rejected', 'cancelled'];

/**
 * Autorizaciones: the requests the user can decide now, the ones they asked for and (with automation.read) every
 * request of the company. Approve or reject from the list, or open the document to see it whole.
 */
@Component({
  selector: 'br-approvals',
  imports: [FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTooltipModule, TPipe, DateTimePipe, MoneyPipe, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.approvals' | t" icon="approval" [subtitle]="'approvals.hint' | t">
        @if (canConfigure()) {
          <a mat-stroked-button [routerLink]="nav.to('configuracion/flujos')"><mat-icon>account_tree</mat-icon>{{ 'nav.workflows' | t }}</a>
        }
      </br-page-header>

      <div class="cl-toolbar-row br-filters">
        <mat-button-toggle-group [ngModel]="view()" (ngModelChange)="setView($event)" [hideSingleSelectionIndicator]="true">
          <mat-button-toggle value="mine">{{ 'approvals.views.mine' | t }}@if (inbox.pendingApprovals() > 0) { ({{ inbox.pendingApprovals() }}) }</mat-button-toggle>
          <mat-button-toggle value="requested">{{ 'approvals.views.requested' | t }}</mat-button-toggle>
          @if (canSeeAll()) {
            <mat-button-toggle value="all">{{ 'approvals.views.all' | t }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'automation.document' | t }}</mat-label>
          <mat-select [ngModel]="document()" (ngModelChange)="document.set($event); page.set(1)">
            <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
            @for (d of documents(); track d.document) {
              <mat-option [value]="d.document">{{ catalogs.name(d) }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (view() !== 'mine') {
          <mat-form-field class="br-filter" subscriptSizing="dynamic">
            <mat-label>{{ 'fields.status' | t }}</mat-label>
            <mat-select [ngModel]="status()" (ngModelChange)="status.set($event); page.set(1)">
              <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
              @for (s of statuses; track s) {
                <mat-option [value]="s">{{ 'enums.workflowStatus.' + s | t }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
      </div>

      <div class="cl-table-wrapper br-table-wrapper">
        <table class="br-table">
          <thead>
            <tr>
              <th>{{ 'automation.document' | t }}</th>
              <th class="br-wide">{{ 'approvals.workflow' | t }}</th>
              <th>{{ 'approvals.step' | t }}</th>
              <th class="br-wide">{{ 'approvals.requested' | t }}</th>
              <th class="br-end br-wide">{{ 'fields.amount' | t }}</th>
              <th>{{ 'fields.status' | t }}</th>
              <th class="cl-actions-cell"></th>
            </tr>
          </thead>
          <tbody>
            @for (a of items(); track a.id) {
              <tr>
                <td>
                  <a class="cl-row-link" [routerLink]="nav.to(a.link)">{{ catalogs.documentName(catalog(), a.document) }} {{ a.reference }}</a>
                </td>
                <td class="br-wide">
                  {{ a.definitionName }}
                  @if (a.fromGlobalDefinition) {
                    <span class="cl-chip cl-chip--info">{{ 'automation.scope.global' | t }}</span>
                  }
                </td>
                <td>
                  @if (a.status === 'pending') {
                    {{ 'approvals.stepOf' | t: { n: a.currentStep + 1, total: a.stepCount } }}
                    <div class="br-sub">{{ a.stepName }}</div>
                  } @else {
                    {{ a.completedAt | dt }}
                    @if (a.closingComment) {
                      <div class="br-sub">{{ a.closingComment }}</div>
                    }
                  }
                </td>
                <td class="br-wide">{{ a.requestedByName }}<div class="br-sub">{{ a.requestedAt | dt }}</div></td>
                <td class="br-end br-wide">{{ a.amount === null ? '—' : (a.amount | money) }}</td>
                <td>
                  <br-status enum="workflowStatus" [value]="a.status" />
                  @if (a.overdue) {
                    <span class="cl-chip cl-chip--error" [matTooltip]="'approvals.overdueHint' | t">{{ 'approvals.overdue' | t }}</span>
                  }
                </td>
                <td class="cl-actions-cell br-nowrap">
                  @if (a.canDecide) {
                    <button mat-icon-button type="button" (click)="decide(a, true)" [disabled]="busy()" [matTooltip]="'workflow.approve' | t"><mat-icon>thumb_up</mat-icon></button>
                    <button mat-icon-button type="button" (click)="decide(a, false)" [disabled]="busy()" [matTooltip]="'workflow.reject' | t"><mat-icon>thumb_down</mat-icon></button>
                  }
                </td>
              </tr>
            } @empty {
              @if (!data.isLoading()) {
                <tr>
                  <td colspan="7" class="br-table-empty">{{ (view() === 'mine' ? 'approvals.nothingPending' : 'common.noRecords') | t }}</td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>
      @if (total() > pageSize) {
        <div class="br-actions br-pager">
          <button mat-button type="button" [disabled]="page() === 1" (click)="page.set(page() - 1)"><mat-icon>chevron_left</mat-icon>{{ 'common.previous' | t }}</button>
          <span class="cl-muted">{{ page() }} / {{ pages() }}</span>
          <button mat-button type="button" [disabled]="page() >= pages()" (click)="page.set(page() + 1)">{{ 'common.next' | t }}<mat-icon iconPositionEnd>chevron_right</mat-icon></button>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApprovalsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  protected readonly catalogs = inject(AutomationCatalogs);
  protected readonly inbox = inject(InboxService);
  protected readonly nav = inject(BrocalNav);
  protected readonly statuses = STATUSES;
  protected readonly pageSize = 25;

  protected readonly view = signal<ApprovalView>('mine');
  protected readonly document = signal<AutomationDocument | null>(null);
  protected readonly status = signal<WorkflowStatus | null>(null);
  protected readonly page = signal(1);
  protected readonly busy = signal(false);
  private readonly reload = signal(0);

  protected readonly canSeeAll = computed(() => this.session.can(P.automationRead, P.automationManage));
  protected readonly canConfigure = computed(() => this.session.can(P.automationRead, P.automationManage));

  protected readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });
  protected readonly documents = computed(() => (this.catalog()?.documents ?? []).filter((d) => d.workflow && d.enabled));

  protected readonly data = apiResource({
    params: () => ({
      view: this.view(),
      document: this.document(),
      status: this.view() === 'mine' ? null : this.status(),
      page: this.page(),
      pageSize: this.pageSize,
      reload: this.reload(),
    }),
    stream: ({ params }) => {
      const { reload: _, ...query } = params;
      return this.api.get<PagedResult<ApprovalSummary>>('automation/approvals', query);
    },
  });

  protected readonly items = computed(() => this.data.value()?.items ?? []);
  protected readonly total = computed(() => this.data.value()?.totalCount ?? 0);
  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));

  protected setView(view: ApprovalView): void {
    this.view.set(view);
    this.page.set(1);
  }

  protected async decide(approval: ApprovalSummary, approve: boolean): Promise<void> {
    const title = this.i18n.t(approve ? 'workflow.approveTitle' : 'workflow.rejectTitle', { reference: approval.reference });
    const confirm = this.i18n.t(approve ? 'workflow.approve' : 'workflow.reject');
    const comment = approve
      ? (await this.dialogs.ask(title, this.i18n.t('workflow.comment'), { message: approval.definitionName, confirm }))?.value
      : await this.dialogs.prompt(title, this.i18n.t('fields.reason'), { message: approval.definitionName, confirm });
    if (comment === undefined || (!approve && !comment)) {
      return;
    }

    this.busy.set(true);
    try {
      const detail = await firstValueFrom(this.api.post<ApprovalDetail>(`automation/approvals/${approval.id}/decision`, { approve, comment }));
      this.dialogs.success(this.i18n.t(detail.summary.status === 'pending' ? 'workflow.decisionSaved' : approve ? 'workflow.approvedOk' : 'workflow.rejectedOk'));
      this.reload.update((r) => r + 1);
      void this.inbox.refresh();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
