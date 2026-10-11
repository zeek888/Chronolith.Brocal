import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Observable } from 'rxjs';
import { I18n, TPipe } from '../../core/i18n';
import { EntityForm } from './entity-form';
import { FieldDef, buildForm, readForm } from './fields';

export interface EntityDialogData {
  title: string;
  fields: FieldDef[];
  value: Record<string, unknown> | null;
  record?: Record<string, unknown> | null;
  editing: boolean;
  /** Saves the form value; the dialog closes with its result. */
  save: (value: Record<string, unknown>) => Observable<unknown>;
}

/** Create/edit dialog of any record described with field definitions. */
@Component({
  selector: 'br-entity-dialog',
  imports: [MatDialogModule, MatButtonModule, MatProgressBarModule, TPipe, EntityForm],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    @if (saving()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <mat-dialog-content>
      <form (submit)="$event.preventDefault(); submit()">
        <br-entity-form [fields]="data.fields" [form]="form" [editing]="data.editing" [record]="data.record ?? null" />
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | t }}</button>
      <button mat-flat-button type="button" (click)="submit()" [disabled]="saving()">{{ 'common.save' | t }}</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityDialog {
  protected readonly data = inject<EntityDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<EntityDialog>);
  private readonly i18n = inject(I18n);

  protected readonly form: FormGroup = buildForm(this.data.fields, this.data.value);
  protected readonly saving = signal(false);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.data.save(readForm(this.data.fields, this.form)).subscribe({
      next: (result) => this.ref.close(result ?? true),
      error: () => this.saving.set(false),
    });
  }

  protected t(key: string): string {
    return this.i18n.t(key);
  }
}
