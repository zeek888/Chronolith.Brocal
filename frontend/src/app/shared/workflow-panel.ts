import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, firstValueFrom, map, of, switchMap } from 'rxjs';
import { BrocalApi } from '../core/api';
import { ApprovalNotices } from '../core/approvals';
import { AutomationCatalogs } from '../core/automation';
import { DateTimePipe } from '../core/format';
import { I18n, TPipe } from '../core/i18n';
import { ApprovalDetail, ApprovalStepState, AutomationDocument, DocumentWorkflow, Lookup, RequestApprovalResult } from '../core/models';
import { BrocalNav } from '../core/nav';
import { InboxService } from '../core/notifications';
import { apiResource } from '../core/resource';
import { BrocalSession } from '../core/session';
import { Dialogs } from './dialogs';
import { StatusChip } from './ui';

const STEP_ICONS: Record<ApprovalStepState, string> = {
  approved: 'check_circle',
  current: 'pending',
  waiting: 'radio_button_unchecked',
  rejected: 'cancel',
  cancelled: 'block',
};

/**
 * Approval workflow of a document: the request in progress (or the last one) with its steps, approvers and
 * decisions, the actions of the current user (approve, reject, withdraw, ask again) and the previous requests.
 * Hidden for customer portal users and for documents without workflows.
 */
@Component({
  selector: 'br-workflow-panel',
  imports: [RouterLink, MatButtonModule, MatIconModule, TPipe, DateTimePipe, StatusChip],
  template: `
    @if (workflow(); as w) {
      @if (w.current || w.applies || w.history.length > 0) {
        <section class="br-card br-workflow" [class.br-workflow--pending]="w.current?.summary?.status === 'pending'">
          <div class="br-card__header">
            <h2><mat-icon>account_tree</mat-icon>{{ 'workflow.title' | t }}</h2>
            @if (w.current; as c) {
              <br-status enum="workflowStatus" [value]="c.summary.status" />
            }
          </div>

          @if (w.current; as c) {
            <p class="br-workflow__meta">
              <strong>{{ c.summary.definitionName }}</strong>
              @if (c.summary.fromGlobalDefinition) {
                <span class="cl-chip cl-chip--info">{{ 'automation.scope.global' | t }}</span>
              }
              <span class="cl-muted">{{ 'workflow.requestedBy' | t: { name: c.summary.requestedByName, date: (c.summary.requestedAt | dt) } }}</span>
            </p>
            @if (reason(); as reason) {
              <p class="cl-muted br-workflow__why"><mat-icon class="br-inline-icon">info</mat-icon>{{ 'workflow.because' | t: { reason } }}</p>
            }

            <ol class="br-steps">
              @for (step of c.steps; track step.index) {
                <li [class]="'br-step br-step--' + step.state">
                  <mat-icon>{{ icons[step.state] }}</mat-icon>
                  <div>
                    <strong>{{ step.name }}</strong>
                    <small>
                      {{ 'workflow.stepState.' + step.state | t }}
                      @if (step.requiredApprovals > 1) {
                        · {{ 'workflow.approvalsNeeded' | t: { n: step.requiredApprovals } }}
                      }
                      @if (step.state === 'current' && step.slaHours) {
                        · {{ 'workflow.sla' | t: { hours: step.slaHours } }}
                      }
                    </small>
                    <small class="cl-muted">{{ step.approverNames.length > 0 ? step.approverNames.join(', ') : ('workflow.noApprovers' | t) }}</small>
                  </div>
                </li>
              }
            </ol>

            @if (c.decisions.length > 0) {
              <ul class="br-timeline">
                @for (d of c.decisions; track $index) {
                  <li>
                    <mat-icon>{{ d.approved ? 'thumb_up' : 'thumb_down' }}</mat-icon>
                    <div>
                      {{ (d.approved ? 'workflow.approvedBy' : 'workflow.rejectedBy') | t: { name: d.name, step: c.steps[d.step]?.name ?? '' } }}
                      <small>{{ d.at | dt }}</small>
                      @if (d.comment) {
                        <small class="br-pre">{{ d.comment }}</small>
                      }
                    </div>
                  </li>
                }
              </ul>
            }

            @if (c.summary.status === 'cancelled' && c.summary.closingComment) {
              <div class="br-warning-box br-warning-box--info"><mat-icon>block</mat-icon>{{ 'workflow.cancelledBecause' | t: { reason: c.summary.closingComment } }}</div>
            }
            @if (c.summary.status === 'rejected') {
              <div class="br-warning-box br-warning-box--error"><mat-icon>thumb_down</mat-icon>{{ 'workflow.rejectedHint' | t }}</div>
            }
          } @else if (w.applies) {
            <p class="br-workflow__meta"><mat-icon class="br-inline-icon">info</mat-icon>{{ 'workflow.willApply' | t: { name: w.definitionName ?? '' } }}</p>
          }

          <div class="br-actions">
            @if (w.current?.summary?.canDecide) {
              <button mat-flat-button type="button" (click)="decide(true)" [disabled]="busy()"><mat-icon>thumb_up</mat-icon>{{ 'workflow.approve' | t }}</button>
              <button mat-stroked-button type="button" (click)="decide(false)" [disabled]="busy()"><mat-icon>thumb_down</mat-icon>{{ 'workflow.reject' | t }}</button>
            }
            @if (w.canRequest) {
              <button mat-stroked-button type="button" (click)="request()" [disabled]="busy()"><mat-icon>send</mat-icon>{{ 'workflow.request' | t }}</button>
            }
            @if (w.current?.canCancel) {
              <button mat-button type="button" (click)="cancel()" [disabled]="busy()"><mat-icon>block</mat-icon>{{ 'workflow.withdraw' | t }}</button>
            }
            <span class="cl-spacer"></span>
            <a mat-button [routerLink]="nav.to('autorizaciones')"><mat-icon>approval</mat-icon>{{ 'nav.approvals' | t }}</a>
          </div>

          @if (w.history.length > 0) {
            <details class="br-workflow__history">
              <summary>{{ 'workflow.history' | t: { n: w.history.length } }}</summary>
              <ul>
                @for (h of w.history; track h.id) {
                  <li>
                    <br-status enum="workflowStatus" [value]="h.status" />
                    {{ h.definitionName }} · {{ h.requestedAt | dt }}
                    @if (h.closingComment) {
                      <span class="cl-muted">· {{ h.closingComment }}</span>
                    }
                  </li>
                }
              </ul>
            </details>
          }
        </section>
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkflowPanel {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  private readonly notices = inject(ApprovalNotices);
  private readonly inbox = inject(InboxService);
  private readonly catalogs = inject(AutomationCatalogs);
  protected readonly nav = inject(BrocalNav);
  protected readonly icons = STEP_ICONS;

  readonly document = input.required<AutomationDocument>();
  readonly documentId = input.required<string>();
  /** Anything that changes when the document reloads (its status, a reload counter). */
  readonly version = input<unknown>(null);
  /** A decision or a request changed the document (an approval can approve or validate it). */
  readonly changed = output<void>();
  /** Workflow of the document once loaded: pages hide their direct approval while a request is pending. */
  readonly loaded = output<DocumentWorkflow | null>();

  protected readonly busy = signal(false);
  private readonly reload = signal(0);

  private readonly data = apiResource({
    params: () =>
      this.session.isPortalUser() ? undefined : { document: this.document(), id: this.documentId(), version: this.version(), held: this.notices.held(), reload: this.reload() },
    stream: ({ params }) => this.api.get<DocumentWorkflow>(`automation/documents/${params.document}/${params.id}`),
  });

  protected readonly workflow = computed(() => this.data.value());

  constructor() {
    effect(() => this.loaded.emit(this.workflow() ?? null));
  }

  private readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });

  private readonly info = computed(() => this.catalog()?.documents.find((d) => d.document === this.document()));

  /** Names of the records used by the conditions of the request (customers, sites…). */
  private readonly names = toSignal(
    toObservable(computed(() => this.catalogs.referenceCatalogs(this.info(), this.workflow()?.current?.conditions ?? []))).pipe(
      switchMap((catalogs) =>
        catalogs.length === 0
          ? of(new Map<string, string>())
          : combineLatest(catalogs.map((c) => this.catalogs.lookupNames(c).pipe(catchError(() => of([] as Lookup[]))))).pipe(
              map((lists) => new Map(lists.flat().map((l) => [l.id, l.name] as [string, string]))),
            ),
      ),
    ),
    { initialValue: new Map<string, string>() },
  );

  /** Why the workflow applied: its conditions with the values the document had. */
  protected readonly reason = computed(() => {
    const current = this.workflow()?.current;
    if (!current || current.conditions.length === 0) {
      return null;
    }

    const glue = ` ${this.i18n.t(current.match === 'all' ? 'automation.and' : 'automation.or')} `;
    return current.conditions.map((c) => this.catalogs.describe(this.info(), c, this.names(), current.facts[c.field] ?? null)).join(glue);
  });

  protected async decide(approve: boolean): Promise<void> {
    const current = this.workflow()?.current;
    if (!current) {
      return;
    }

    const title = this.i18n.t(approve ? 'workflow.approveTitle' : 'workflow.rejectTitle', { reference: current.summary.reference });
    const confirm = this.i18n.t(approve ? 'workflow.approve' : 'workflow.reject');
    // The approval comment is optional; a rejection needs its reason.
    const comment = approve
      ? (await this.dialogs.ask(title, this.i18n.t('workflow.comment'), { message: current.summary.definitionName, confirm }))?.value
      : await this.dialogs.prompt(title, this.i18n.t('fields.reason'), { message: current.summary.definitionName, confirm });
    if (comment === undefined || (!approve && !comment)) {
      return;
    }

    await this.run(async () => {
      const detail = await firstValueFrom(this.api.post<ApprovalDetail>(`automation/approvals/${current.summary.id}/decision`, { approve, comment }));
      this.dialogs.success(this.i18n.t(detail.summary.status === 'pending' ? 'workflow.decisionSaved' : approve ? 'workflow.approvedOk' : 'workflow.rejectedOk'));
    });
  }

  protected async cancel(): Promise<void> {
    const current = this.workflow()?.current;
    const reason = current ? await this.dialogs.prompt(this.i18n.t('workflow.withdrawTitle'), this.i18n.t('fields.reason'), { confirm: this.i18n.t('workflow.withdraw') }) : null;
    if (current && reason) {
      await this.run(() => firstValueFrom(this.api.post(`automation/approvals/${current.summary.id}/cancel`, { reason })));
    }
  }

  protected async request(): Promise<void> {
    await this.run(async () => {
      const result = await firstValueFrom(this.api.post<RequestApprovalResult>(`automation/documents/${this.document()}/${this.documentId()}/request`));
      this.dialogs.info(result.message);
    });
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.reload.update((r) => r + 1);
      void this.inbox.refresh();
      this.changed.emit();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
