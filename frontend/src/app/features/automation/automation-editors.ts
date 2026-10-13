import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AutomationCatalogs, OPERATORS } from '../../core/automation';
import { TPipe } from '../../core/i18n';
import { Audience, AutomationCatalog, AutomationDocumentInfo, ConditionOperator, FactDefinition, ModuleInfo, Role, RuleCondition } from '../../core/models';
import { LookupSelect } from '../../shared/lookup-select';

// ---- Form models --------------------------------------------------------------------------------------------

export type ConditionForm = FormGroup<{
  field: FormControl<string | null>;
  operator: FormControl<ConditionOperator | null>;
  value: FormControl<unknown>;
}>;

export type AudienceForm = FormGroup<{
  roleTemplates: FormControl<string[]>;
  roleIds: FormControl<string[]>;
  permissions: FormControl<string[]>;
  tenantAdmins: FormControl<boolean>;
}>;

const MULTI: ConditionOperator[] = ['in', 'notIn'];

export function isMulti(operator: ConditionOperator | null | undefined): boolean {
  return !!operator && MULTI.includes(operator);
}

/** Form row of a condition: list values become arrays, amounts numbers. */
export function conditionForm(condition: RuleCondition | null, fact: FactDefinition | undefined): ConditionForm {
  let value: unknown = condition?.value ?? null;
  if (fact && condition?.value !== null && condition?.value !== undefined) {
    if (fact.type === 'number' || fact.type === 'money') {
      value = Number(condition.value);
    } else if ((fact.type === 'enum' || fact.type === 'reference') && isMulti(condition.operator)) {
      value = condition.value.split(',').filter(Boolean);
    }
  }

  return new FormGroup({
    field: new FormControl<string | null>(condition?.field ?? null, Validators.required),
    operator: new FormControl<ConditionOperator | null>(condition?.operator ?? null, Validators.required),
    value: new FormControl<unknown>(value),
  });
}

/** Condition for the API: values as invariant text, lists joined with commas. */
export function readCondition(form: ConditionForm, fact: FactDefinition | undefined): RuleCondition {
  const { field, operator } = form.getRawValue();
  const raw = form.controls.value.value;
  let value: string | null = null;
  if (fact?.type !== 'boolean' && raw !== null && raw !== undefined && raw !== '') {
    value = Array.isArray(raw) ? raw.join(',') : String(raw);
  }

  return { field: field ?? '', operator: operator ?? 'equal', value };
}

export function audienceForm(audience: Audience | null | undefined): AudienceForm {
  return new FormGroup({
    roleTemplates: new FormControl<string[]>(audience?.roleTemplates ?? [], { nonNullable: true }),
    roleIds: new FormControl<string[]>(audience?.roleIds ?? [], { nonNullable: true }),
    permissions: new FormControl<string[]>(audience?.permissions ?? [], { nonNullable: true }),
    tenantAdmins: new FormControl<boolean>(audience?.tenantAdmins ?? false, { nonNullable: true }),
  });
}

export function readAudience(form: AudienceForm, global: boolean): Audience {
  const value = form.getRawValue();
  return { ...value, roleIds: global ? [] : value.roleIds };
}

export function audienceIsEmpty(audience: Audience): boolean {
  return audience.roleTemplates.length === 0 && audience.roleIds.length === 0 && audience.permissions.length === 0 && !audience.tenantAdmins;
}

// ---- Conditions ---------------------------------------------------------------------------------------------

/**
 * Builder of conditions: datum of the document, comparison and value (amount, list of options, records of a
 * catalog or nothing for yes/no data). Global definitions cannot use records of one company.
 */
@Component({
  selector: 'br-conditions-editor',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, MatTooltipModule, TPipe, LookupSelect],
  template: `
    <div class="br-conditions">
      @for (row of conditions().controls; track row; let i = $index) {
        <div class="br-condition" [formGroup]="row">
          <span class="br-condition__glue">{{ i === 0 ? ('automation.when' | t) : ((match() === 'all' ? 'automation.and' : 'automation.or') | t) }}</span>
          <mat-form-field subscriptSizing="dynamic">
            <mat-label>{{ 'automation.field' | t }}</mat-label>
            <mat-select formControlName="field" (selectionChange)="fieldChanged(row)">
              @for (fact of facts(); track fact.key) {
                <mat-option [value]="fact.key">{{ catalogs.name(fact) }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          @if (factOf(row); as fact) {
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'automation.operator' | t }}</mat-label>
              <mat-select formControlName="operator" (selectionChange)="operatorChanged(row)">
                @for (op of operatorsOf(fact); track op) {
                  <mat-option [value]="op">{{ 'automation.operators.' + op | t }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            @if (fact.type === 'number' || fact.type === 'money') {
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ (fact.type === 'money' ? 'fields.amount' : 'automation.value') | t }}</mat-label>
                <input matInput type="number" formControlName="value" />
                @if (fact.type === 'money') {
                  <span matTextPrefix>$&nbsp;</span>
                }
              </mat-form-field>
            } @else if (fact.type === 'enum') {
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'automation.value' | t }}</mat-label>
                <mat-select formControlName="value" [multiple]="multi(row)">
                  @for (option of fact.values ?? []; track option) {
                    <mat-option [value]="option">{{ fact.enumName ? (('enums.' + fact.enumName + '.' + option) | t) : option }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
            } @else if (fact.type === 'reference') {
              <br-lookup class="br-condition__lookup" formControlName="value" [catalog]="fact.catalog" [multiple]="multi(row)" [label]="'automation.value' | t" [includeInactive]="true" />
            }
          }
          @if (!readonly()) {
            <button mat-icon-button type="button" (click)="remove(i)" [matTooltip]="'common.remove' | t"><mat-icon>close</mat-icon></button>
          }
        </div>
      } @empty {
        <p class="cl-muted">{{ emptyText() | t }}</p>
      }
      @if (!readonly()) {
        <button mat-stroked-button type="button" (click)="add()" [disabled]="!document()"><mat-icon>add</mat-icon>{{ 'automation.addCondition' | t }}</button>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class ConditionsEditor {
  protected readonly catalogs = inject(AutomationCatalogs);

  readonly conditions = input.required<FormArray<ConditionForm>>();
  readonly document = input<AutomationDocumentInfo | undefined>(undefined);
  readonly global = input(false);
  readonly match = input<'all' | 'any'>('all');
  readonly readonly = input(false);
  readonly emptyText = input('automation.noConditions');

  /** Global definitions only test data every company has. */
  protected readonly facts = computed(() => (this.document()?.facts ?? []).filter((f) => !this.global() || !f.companyOnly));

  protected factOf(row: ConditionForm): FactDefinition | undefined {
    return this.catalogs.fact(this.document(), row.controls.field.value ?? '');
  }

  protected operatorsOf(fact: FactDefinition): ConditionOperator[] {
    return OPERATORS[fact.type];
  }

  protected multi(row: ConditionForm): boolean {
    return isMulti(row.controls.operator.value);
  }

  protected add(): void {
    this.conditions().push(conditionForm(null, undefined));
  }

  protected remove(index: number): void {
    this.conditions().removeAt(index);
    this.conditions().markAsDirty();
  }

  protected fieldChanged(row: ConditionForm): void {
    const fact = this.factOf(row);
    const operator = fact ? OPERATORS[fact.type][0] : null;
    row.controls.operator.setValue(operator);
    row.controls.value.setValue(isMulti(operator) ? [] : null);
  }

  /** Lists of values (in / not in) take several options; the other comparisons one. */
  protected operatorChanged(row: ConditionForm): void {
    const value = row.controls.value.value;
    if (this.multi(row) && !Array.isArray(value)) {
      row.controls.value.setValue(value === null || value === undefined || value === '' ? [] : [value]);
    } else if (!this.multi(row) && Array.isArray(value)) {
      row.controls.value.setValue(value[0] ?? null);
    }
  }
}

// ---- Audience -----------------------------------------------------------------------------------------------

/**
 * Who approves a step or hears about a rule: role templates (they exist in every company), roles of the company,
 * holders of a permission and the company administrators.
 */
@Component({
  selector: 'br-audience-editor',
  imports: [ReactiveFormsModule, MatCheckboxModule, MatFormFieldModule, MatSelectModule, TPipe],
  template: `
    <div class="br-audience" [formGroup]="group()">
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>{{ 'automation.audience.roleTemplates' | t }}</mat-label>
        <mat-select formControlName="roleTemplates" multiple>
          @for (template of catalog()?.roleTemplates ?? []; track template.key) {
            <mat-option [value]="template.key">{{ catalogs.roleTemplateName(catalog(), template.key) }}</mat-option>
          }
        </mat-select>
        <mat-hint>{{ 'automation.audience.roleTemplatesHint' | t }}</mat-hint>
      </mat-form-field>
      @if (!global()) {
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>{{ 'automation.audience.roles' | t }}</mat-label>
          <mat-select formControlName="roleIds" multiple>
            @for (role of roles(); track role.id) {
              <mat-option [value]="role.id">{{ role.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>{{ 'automation.audience.permissions' | t }}</mat-label>
        <mat-select formControlName="permissions" multiple>
          @for (module of modules(); track module.code) {
            <mat-optgroup [label]="catalogs.name(module)">
              @for (permission of module.permissions; track permission.code) {
                <mat-option [value]="permission.code">{{ catalogs.name(permission) }}</mat-option>
              }
            </mat-optgroup>
          }
        </mat-select>
      </mat-form-field>
      <mat-checkbox formControlName="tenantAdmins">{{ 'automation.audience.tenantAdmins' | t }}</mat-checkbox>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class AudienceEditor {
  protected readonly catalogs = inject(AutomationCatalogs);

  readonly group = input.required<AudienceForm>();
  readonly global = input(false);
  readonly catalog = input<AutomationCatalog | null>(null);
  readonly roles = input<Role[]>([]);
  readonly modules = input<ModuleInfo[]>([]);
}
