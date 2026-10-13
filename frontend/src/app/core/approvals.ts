import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { I18n } from './i18n';
import { ApprovalPending } from './models';

export function isApprovalPending(body: unknown): body is ApprovalPending {
  return !!body && typeof body === 'object' && (body as ApprovalPending).approvalPending === true;
}

/**
 * Actions held by an approval workflow (the API answers 202 instead of doing them): the message is shown and the
 * pages with a workflow panel refresh it. A success toast right after the notice does not hide it.
 */
@Injectable({ providedIn: 'root' })
export class ApprovalNotices {
  private readonly snack = inject(MatSnackBar);
  private readonly i18n = inject(I18n);
  private lastShown = 0;

  /** Increments with every held action (workflow panels reload when it changes). */
  readonly held = signal(0);

  notify(body: unknown): void {
    if (!isApprovalPending(body)) {
      return;
    }

    this.lastShown = Date.now();
    this.snack.open(`${this.i18n.t('workflow.held')} ${body.message}`, this.i18n.t('common.close'), { duration: 9000 });
    this.held.update((n) => n + 1);
  }

  /** A held action was answered a moment ago (its toast must stay visible). */
  recentlyShown(): boolean {
    return Date.now() - this.lastShown < 2500;
  }
}
