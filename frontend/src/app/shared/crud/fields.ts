import { AbstractControl, FormArray, FormControl, FormGroup, ValidatorFn, Validators } from '@angular/forms';
import { Query } from '../../core/api';
import { FileCategory, Lookup } from '../../core/models';
import { Column } from '../data-table';

export type FieldType =
  | 'text'
  | 'textarea'
  | 'number'
  | 'money'
  | 'percent'
  | 'date'
  | 'datetime'
  | 'checkbox'
  | 'select'
  | 'multiselect'
  | 'enum'
  | 'lookup'
  | 'lookups'
  | 'color'
  | 'chips'
  | 'array'
  | 'section'
  | 'file';

export interface FieldOption {
  value: string | number | boolean | null;
  /** Translation key (or literal text when `literal`). */
  label: string;
  literal?: boolean;
}

export interface FieldDef {
  key: string;
  /** Translation key. */
  label: string;
  type: FieldType;
  required?: boolean;
  /** Options of a `select`. */
  options?: FieldOption[];
  /** Enum name (translations `enums.<enum>.<value>`) and its values for `enum` fields. */
  enum?: string;
  values?: readonly string[];
  /** Catalog of `lookup`/`lookups` fields (`catalogs/customers`). */
  catalog?: string;
  query?: Query;
  /** Filters the options by the value of another field (lookup `parentId`), keeping general items. */
  parent?: string;
  min?: number;
  max?: number;
  maxLength?: number;
  pattern?: string;
  /** Upper case on blur (codes, RFC). */
  upper?: boolean;
  hint?: string;
  span?: 'full';
  /** Sub-fields of an `array` (rows such as contacts, price list items, checklist questions). */
  fields?: FieldDef[];
  addLabel?: string;
  defaultValue?: unknown;
  category?: FileCategory;
  /** URL of the current file (`file` fields) read from the record. */
  fileUrlKey?: string;
  showIf?: (value: Record<string, unknown>) => boolean;
  /** Not editable once created (codes used as keys elsewhere). */
  lockedOnEdit?: boolean;
}

export interface FilterDef {
  key: string;
  label: string;
  type: 'enum' | 'lookup' | 'bool';
  enum?: string;
  values?: readonly string[];
  catalog?: string;
}

export interface CatalogConfig<T = Record<string, unknown>> {
  /** Translation key of the title (`nav.<key>`) and of the singular (`catalogs.<key>`). */
  key: string;
  /** API route of the catalog (`catalogs/customers`). */
  api: string;
  icon: string;
  /** Permissions to create, edit, (de)activate and delete. */
  manage: readonly string[];
  columns: Column<T>[];
  fields: FieldDef[];
  filters?: FilterDef[];
  defaults?: () => Record<string, unknown>;
  /** DTO → form values (default: same keys). */
  toForm?: (dto: T) => Record<string, unknown>;
  /** Form values → payload of the API (default: the raw value). */
  toPayload?: (value: Record<string, unknown>) => unknown;
  /** Detail page of a record instead of the edit dialog (units, employees). */
  detail?: (row: T) => string[];
  dialogWidth?: string;
  /** Lookups to refresh after saving (default: the catalog itself). */
  invalidates?: string[];
}

export function lookupFilter(parentValue: unknown): (item: Lookup) => boolean {
  return (item) => !parentValue || !item.parentId || item.parentId === parentValue;
}

/** Builds the reactive form of a set of fields (recursively for arrays) with the record values. */
export function buildForm(fields: FieldDef[], value: Record<string, unknown> | null): FormGroup {
  const controls: Record<string, AbstractControl> = {};
  for (const field of fields) {
    if (field.type === 'section') {
      continue;
    }

    const current = value?.[field.key];
    if (field.type === 'array') {
      const rows = Array.isArray(current) ? (current as Record<string, unknown>[]) : [];
      controls[field.key] = new FormArray(rows.map((row) => buildForm(field.fields ?? [], row)));
      continue;
    }

    let initial = current === undefined ? (field.defaultValue ?? defaultFor(field)) : current;
    if (field.type === 'percent' && typeof initial === 'number') {
      initial = round(initial * 100, 4);
    }

    controls[field.key] = new FormControl(initial, validatorsOf(field));
  }

  return new FormGroup(controls);
}

/** Reads the form back into the API shape (percent fields go back to fractions). */
export function readForm(fields: FieldDef[], group: FormGroup): Record<string, unknown> {
  const result: Record<string, unknown> = {};
  for (const field of fields) {
    if (field.type === 'section') {
      continue;
    }

    const control = group.get(field.key);
    if (field.type === 'array') {
      result[field.key] = ((control as FormArray | null)?.controls ?? []).map((row) => readForm(field.fields ?? [], row as FormGroup));
      continue;
    }

    let value = control?.value as unknown;
    if (typeof value === 'string') {
      value = value.trim() === '' ? null : field.upper ? value.trim().toUpperCase() : value.trim();
    }

    if (field.type === 'percent' && typeof value === 'number') {
      value = round(value / 100, 6);
    }

    result[field.key] = value;
  }

  return result;
}

export function addRow(field: FieldDef, array: FormArray, value: Record<string, unknown> | null = null): void {
  array.push(buildForm(field.fields ?? [], value));
}

function defaultFor(field: FieldDef): unknown {
  switch (field.type) {
    case 'checkbox':
      return false;
    case 'lookups':
    case 'multiselect':
    case 'chips':
      return [];
    default:
      return null;
  }
}

function validatorsOf(field: FieldDef): ValidatorFn[] {
  const validators: ValidatorFn[] = [];
  if (field.required) {
    validators.push(field.type === 'checkbox' ? Validators.requiredTrue : Validators.required);
  }

  if (field.min !== undefined) {
    validators.push(Validators.min(field.min));
  }

  if (field.max !== undefined) {
    validators.push(Validators.max(field.max));
  }

  if (field.maxLength !== undefined) {
    validators.push(Validators.maxLength(field.maxLength));
  }

  if (field.pattern) {
    validators.push(Validators.pattern(field.pattern));
  }

  return validators;
}

function round(value: number, digits: number): number {
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
}
