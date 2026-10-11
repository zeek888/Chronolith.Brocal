import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule, MatChipInputEvent } from '@angular/material/chips';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { fileUrl } from '../../core/config';
import { I18n, TPipe } from '../../core/i18n';
import { Lookup, UploadedFile } from '../../core/models';
import { FileUpload } from '../file-inputs';
import { LookupSelect } from '../lookup-select';
import { FieldDef, addRow, lookupFilter } from './fields';

/**
 * Renders a form from field definitions: text, numbers, money, percentages, dates, enums, catalog lookups
 * (single, multiple and dependent), colors, lists of texts, files and arrays of rows (recursively).
 */
@Component({
  selector: 'br-entity-form',
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatTooltipModule,
    TPipe,
    LookupSelect,
    FileUpload,
  ],
  template: `
    <ng-container [ngTemplateOutlet]="fieldsTpl" [ngTemplateOutletContext]="{ $implicit: fields(), group: form(), nested: false }" />

    <ng-template #fieldsTpl let-fields let-group="group" let-nested="nested">
      <div class="cl-form-grid" [class.br-nested-grid]="nested">
        @for (field of asFields(fields); track field.key) {
          @if (visible(field, group)) {
            @switch (field.type) {
              @case ('section') {
                <h3 class="br-form-section cl-full-row">{{ field.label | t }}</h3>
              }
              @case ('checkbox') {
                <mat-checkbox class="br-checkbox" [class.cl-full-row]="field.span === 'full'" [formControl]="ctrl(group, field.key)">{{ field.label | t }}</mat-checkbox>
              }
              @case ('lookup') {
                <br-lookup
                  [class.cl-full-row]="field.span === 'full'"
                  [formControl]="ctrl(group, field.key)"
                  [catalog]="field.catalog!"
                  [query]="field.query ?? null"
                  [label]="field.label | t"
                  [required]="!!field.required"
                  [filter]="parentFilter(field, group)"
                  [hint]="field.hint ? (field.hint | t) : null"
                />
              }
              @case ('lookups') {
                <br-lookup
                  [class.cl-full-row]="field.span === 'full'"
                  [formControl]="ctrl(group, field.key)"
                  [catalog]="field.catalog!"
                  [query]="field.query ?? null"
                  [label]="field.label | t"
                  [multiple]="true"
                  [filter]="parentFilter(field, group)"
                  [hint]="field.hint ? (field.hint | t) : null"
                />
              }
              @case ('chips') {
                <mat-form-field class="cl-full-row">
                  <mat-label>{{ field.label | t }}</mat-label>
                  <mat-chip-grid #grid>
                    @for (chip of chipValues(group, field.key); track $index) {
                      <mat-chip-row (removed)="removeChip(group, field.key, $index)">
                        {{ chip }}
                        <button matChipRemove type="button"><mat-icon>cancel</mat-icon></button>
                      </mat-chip-row>
                    }
                    <input [matChipInputFor]="grid" (matChipInputTokenEnd)="addChip(group, field.key, $event)" [placeholder]="'common.typeAndEnter' | t" />
                  </mat-chip-grid>
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                </mat-form-field>
              }
              @case ('file') {
                <div class="br-file-field" [class.cl-full-row]="field.span === 'full'">
                  <span class="br-file-field__label">{{ field.label | t }}</span>
                  <br-file-upload [category]="field.category ?? 'document'" [currentName]="ctrl(group, field.key).value ? ('common.currentFile' | t) : null" [currentUrl]="currentFileUrl(field)" (uploaded)="fileUploaded(group, field.key, $event)" />
                </div>
              }
              @case ('array') {
                <div class="br-array cl-full-row">
                  <div class="br-array__header">
                    <h3>{{ field.label | t }}</h3>
                    <button mat-stroked-button type="button" (click)="add(field, group)"><mat-icon>add</mat-icon>{{ (field.addLabel ?? 'common.addRow') | t }}</button>
                  </div>
                  @for (row of rows(group, field.key); track row; let i = $index) {
                    <div class="br-array__row">
                      <ng-container [ngTemplateOutlet]="fieldsTpl" [ngTemplateOutletContext]="{ $implicit: field.fields, group: row, nested: true }" />
                      <button mat-icon-button type="button" class="br-array__remove" (click)="remove(group, field.key, i)" [matTooltip]="'common.remove' | t">
                        <mat-icon>delete</mat-icon>
                      </button>
                    </div>
                  } @empty {
                    <p class="cl-muted br-array__empty">{{ 'common.noRows' | t }}</p>
                  }
                </div>
              }
              @case ('date') {
                <mat-form-field [class.cl-full-row]="field.span === 'full'">
                  <mat-label>{{ field.label | t }}</mat-label>
                  <input matInput [matDatepicker]="picker" [formControl]="ctrl(group, field.key)" />
                  <mat-datepicker-toggle matIconSuffix [for]="picker" />
                  <mat-datepicker #picker />
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                  <mat-error>{{ errorOf(ctrl(group, field.key)) }}</mat-error>
                </mat-form-field>
              }
              @case ('money') {
                <mat-form-field [class.cl-full-row]="field.span === 'full'">
                  <mat-label>{{ field.label | t }}</mat-label>
                  <span matTextPrefix>$&nbsp;</span>
                  <input matInput type="number" step="0.01" [formControl]="ctrl(group, field.key)" [attr.min]="field.min ?? 0" />
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                  <mat-error>{{ errorOf(ctrl(group, field.key)) }}</mat-error>
                </mat-form-field>
              }
              @case ('percent') {
                <mat-form-field [class.cl-full-row]="field.span === 'full'">
                  <mat-label>{{ field.label | t }}</mat-label>
                  <input matInput type="number" step="0.01" [formControl]="ctrl(group, field.key)" />
                  <span matTextSuffix>%</span>
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                  <mat-error>{{ errorOf(ctrl(group, field.key)) }}</mat-error>
                </mat-form-field>
              }
              @case ('color') {
                <mat-form-field [class.cl-full-row]="field.span === 'full'">
                  <mat-label>{{ field.label | t }}</mat-label>
                  <input matInput [formControl]="ctrl(group, field.key)" placeholder="#0F766E" />
                  <input matSuffix type="color" class="br-color-input" [value]="ctrl(group, field.key).value || '#000000'" (input)="setColor(group, field.key, $event)" />
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                  <mat-error>{{ errorOf(ctrl(group, field.key)) }}</mat-error>
                </mat-form-field>
              }
              @default {
                <mat-form-field [class.cl-full-row]="field.span === 'full' || field.type === 'textarea'">
                  <mat-label>{{ field.label | t }}</mat-label>
                  @switch (field.type) {
                    @case ('textarea') {
                      <textarea matInput rows="3" [formControl]="ctrl(group, field.key)"></textarea>
                    }
                    @case ('datetime') {
                      <input matInput type="datetime-local" [formControl]="ctrl(group, field.key)" />
                    }
                    @case ('number') {
                      <input matInput type="number" [formControl]="ctrl(group, field.key)" [attr.min]="field.min ?? null" [attr.max]="field.max ?? null" />
                    }
                    @case ('enum') {
                      <mat-select [formControl]="ctrl(group, field.key)">
                        @if (!field.required) {
                          <mat-option [value]="null">—</mat-option>
                        }
                        @for (value of field.values ?? []; track value) {
                          <mat-option [value]="value">{{ 'enums.' + field.enum + '.' + value | t }}</mat-option>
                        }
                      </mat-select>
                    }
                    @case ('select') {
                      <mat-select [formControl]="ctrl(group, field.key)">
                        @for (option of field.options ?? []; track option.value) {
                          <mat-option [value]="option.value">{{ option.literal ? option.label : (option.label | t) }}</mat-option>
                        }
                      </mat-select>
                    }
                    @case ('multiselect') {
                      <mat-select [formControl]="ctrl(group, field.key)" multiple>
                        @for (option of field.options ?? []; track option.value) {
                          <mat-option [value]="option.value">{{ option.literal ? option.label : (option.label | t) }}</mat-option>
                        }
                      </mat-select>
                    }
                    @default {
                      <input matInput [formControl]="ctrl(group, field.key)" [class.br-upper]="field.upper" [readonly]="locked(field)" />
                    }
                  }
                  @if (field.hint) {
                    <mat-hint>{{ field.hint | t }}</mat-hint>
                  }
                  <mat-error>{{ errorOf(ctrl(group, field.key)) }}</mat-error>
                </mat-form-field>
              }
            }
          }
        }
      </div>
    </ng-template>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class EntityForm implements OnInit {
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);
  private readonly filters = new Map<string, (item: Lookup) => boolean>();

  readonly fields = input.required<FieldDef[]>();
  readonly form = input.required<FormGroup>();
  readonly editing = input(false);
  /** Record being edited (current file links). */
  readonly record = input<Record<string, unknown> | null>(null);

  /** Bumps when any value changes, so dependent fields and `showIf` re-evaluate. */
  protected readonly version = signal(0);

  ngOnInit(): void {
    this.form()
      .valueChanges.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.version.update((v) => v + 1));
  }

  protected asFields(value: unknown): FieldDef[] {
    return value as FieldDef[];
  }

  protected ctrl(group: FormGroup, key: string): FormControl {
    return group.get(key) as FormControl;
  }

  protected rows(group: FormGroup, key: string): FormGroup[] {
    return ((group.get(key) as FormArray | null)?.controls ?? []) as FormGroup[];
  }

  protected visible(field: FieldDef, group: FormGroup): boolean {
    this.version();
    return !field.showIf || field.showIf(group.getRawValue() as Record<string, unknown>);
  }

  protected locked(field: FieldDef): boolean {
    return !!field.lockedOnEdit && this.editing();
  }

  /** Same function instance while the parent value does not change (avoids reloading the options). */
  protected parentFilter(field: FieldDef, group: FormGroup): ((item: Lookup) => boolean) | null {
    if (!field.parent) {
      return null;
    }

    this.version();
    const parentValue = group.get(field.parent)?.value as unknown;
    const key = `${field.key}|${String(parentValue ?? '')}`;
    let filter = this.filters.get(key);
    if (!filter) {
      filter = lookupFilter(parentValue);
      this.filters.set(key, filter);
    }

    return filter;
  }

  protected add(field: FieldDef, group: FormGroup): void {
    addRow(field, group.get(field.key) as FormArray);
  }

  protected remove(group: FormGroup, key: string, index: number): void {
    (group.get(key) as FormArray).removeAt(index);
    group.markAsDirty();
  }

  protected chipValues(group: FormGroup, key: string): string[] {
    return (group.get(key)?.value as string[] | null) ?? [];
  }

  protected addChip(group: FormGroup, key: string, event: MatChipInputEvent): void {
    const value = event.value.trim();
    if (value) {
      const control = group.get(key)!;
      control.setValue([...((control.value as string[] | null) ?? []), value]);
      control.markAsDirty();
    }

    event.chipInput.clear();
  }

  protected removeChip(group: FormGroup, key: string, index: number): void {
    const control = group.get(key)!;
    const values = [...((control.value as string[] | null) ?? [])];
    values.splice(index, 1);
    control.setValue(values);
    control.markAsDirty();
  }

  protected setColor(group: FormGroup, key: string, event: Event): void {
    const control = group.get(key)!;
    control.setValue((event.target as HTMLInputElement).value.toUpperCase());
    control.markAsDirty();
  }

  protected fileUploaded(group: FormGroup, key: string, file: UploadedFile | null): void {
    const control = group.get(key)!;
    control.setValue(file?.id ?? null);
    control.markAsDirty();
  }

  protected currentFileUrl(field: FieldDef): string | null {
    const record = this.record();
    return field.fileUrlKey && record ? fileUrl(record[field.fileUrlKey] as string | null) : null;
  }

  protected errorOf(control: AbstractControl): string {
    const errors = control.errors ?? {};
    if (errors['required']) {
      return this.i18n.t('validation.required');
    }

    if (errors['min']) {
      return this.i18n.t('validation.min', { min: (errors['min'] as { min: number }).min });
    }

    if (errors['max']) {
      return this.i18n.t('validation.max', { max: (errors['max'] as { max: number }).max });
    }

    if (errors['maxlength']) {
      return this.i18n.t('validation.maxLength', { max: (errors['maxlength'] as { requiredLength: number }).requiredLength });
    }

    if (errors['pattern']) {
      return this.i18n.t('validation.pattern');
    }

    if (errors['matDatepickerParse']) {
      return this.i18n.t('validation.date');
    }

    return errors['server'] ? String(errors['server']) : '';
  }
}

