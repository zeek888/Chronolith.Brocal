import { ChangeDetectionStrategy, Component, Injectable, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { I18n, TPipe } from '../core/i18n';
import { ProblemDetails } from '../core/models';

interface ConfirmData {
  title: string;
  message: string;
  confirm: string;
  danger: boolean;
}

@Component({
  selector: 'br-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, TPipe],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p class="br-pre">{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ 'common.cancel' | t }}</button>
      <button mat-flat-button [class.br-danger]="data.danger" [mat-dialog-close]="true" cdkFocusInitial>{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

interface PromptData {
  title: string;
  message: string | null;
  label: string;
  confirm: string;
  required: boolean;
  value: string | null;
  multiline: boolean;
}

@Component({
  selector: 'br-prompt-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule, TPipe],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (data.message) {
        <p class="br-pre">{{ data.message }}</p>
      }
      <mat-form-field class="br-full">
        <mat-label>{{ data.label }}</mat-label>
        @if (data.multiline) {
          <textarea matInput [formControl]="value" rows="3" cdkFocusInitial></textarea>
        } @else {
          <input matInput [formControl]="value" cdkFocusInitial />
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | t }}</button>
      <button mat-flat-button [disabled]="value.invalid" (click)="ref.close(value.value?.trim() || null)">{{ data.confirm }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PromptDialog {
  protected readonly data = inject<PromptData>(MAT_DIALOG_DATA);
  protected readonly ref = inject(MatDialogRef<PromptDialog, string | null>);
  protected readonly value = new FormControl(this.data.value ?? '', this.data.required ? [Validators.required, Validators.maxLength(1000)] : [Validators.maxLength(1000)]);
}

/** Confirmations, reasons (cancel, reopen, reject) and toasts with the same look in every page. */
@Injectable({ providedIn: 'root' })
export class Dialogs {
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly i18n = inject(I18n);

  confirm(title: string, message: string, confirm?: string, danger = false): Promise<boolean> {
    const ref = this.dialog.open(ConfirmDialog, {
      data: { title, message, confirm: confirm ?? this.i18n.t('common.confirm'), danger } satisfies ConfirmData,
      width: '440px',
    });
    return firstValueFrom(ref.afterClosed()).then((result) => result === true);
  }

  /** Asks for a text (reason, comment); null when cancelled. */
  prompt(title: string, label: string, options?: { message?: string; confirm?: string; required?: boolean; value?: string; multiline?: boolean }): Promise<string | null> {
    const ref = this.dialog.open(PromptDialog, {
      data: {
        title,
        label,
        message: options?.message ?? null,
        confirm: options?.confirm ?? this.i18n.t('common.accept'),
        required: options?.required ?? true,
        value: options?.value ?? null,
        multiline: options?.multiline ?? true,
      } satisfies PromptData,
      width: '480px',
    });
    return firstValueFrom(ref.afterClosed()).then((result: string | null | undefined) => result ?? null);
  }

  success(message: string): void {
    this.snack.open(message, this.i18n.t('common.close'), { duration: 4000, panelClass: 'cl-snack--success' });
  }

  info(message: string): void {
    this.snack.open(message, this.i18n.t('common.close'), { duration: 5000 });
  }

  error(error: unknown): void {
    this.snack.open(describeError(error, this.i18n), this.i18n.t('common.close'), { duration: 8000, panelClass: 'cl-snack--error' });
  }
}

const GENERIC_ERRORS: Record<number, string> = {
  401: 'errors.unauthorized',
  403: 'errors.forbidden',
  404: 'errors.notFound',
  413: 'errors.tooLarge',
  429: 'errors.tooManyRequests',
};

export function describeError(error: unknown, i18n: I18n): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return i18n.t('errors.noConnection');
    }

    const problem = (error.error && typeof error.error === 'object' ? error.error : null) as ProblemDetails | null;
    const fields = Object.values(problem?.errors ?? {}).flat();
    if (fields.length > 0) {
      return fields.slice(0, 3).join(' · ');
    }

    // Business errors carry a detail; the authorization middleware only answers the status with its standard
    // English title ("Forbidden"), so those statuses get their own text.
    const generic = GENERIC_ERRORS[error.status];
    return problem?.detail ?? (generic ? i18n.t(generic) : (problem?.title ?? i18n.t('errors.http', { status: error.status })));
  }

  return error instanceof Error ? error.message : String(error);
}
