import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, firstValueFrom, map, of, switchMap } from 'rxjs';
import { toObservable } from '@angular/core/rxjs-interop';
import { BrocalApi } from '../../core/api';
import { AutomationCatalogs } from '../../core/automation';
import { I18n, TPipe } from '../../core/i18n';
import { ConditionMatch, Lookup, ModuleInfo, Role, WorkflowDefinition, WorkflowStep } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader } from '../../shared/ui';
import { AudienceEditor, AudienceForm, ConditionForm, ConditionsEditor, audienceForm, audienceIsEmpty, conditionForm, readAudience, readCondition } from './automation-editors';

type StepForm = FormGroup<{
  name: FormControl<string>;
  approvers: AudienceForm;
  requiredApprovals: FormControl<number>;
  slaHours: FormControl<number | null>;
  allowRequester: FormControl<boolean>;
}>;

function stepForm(step: WorkflowStep | null): StepForm {
  return new FormGroup({
    name: new FormControl(step?.name ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
    approvers: audienceForm(step?.approvers),
    requiredApprovals: new FormControl(step?.requiredApprovals ?? 1, { nonNullable: true, validators: [Validators.required, Validators.min(1), Validators.max(5)] }),
    slaHours: new FormControl<number | null>(step?.slaHours ?? null, [Validators.min(1), Validators.max(720)]),
    allowRequester: new FormControl(step?.allowRequester ?? false, { nonNullable: true }),
  });
}

/** Names of the records used by reference conditions (customers, sites…), for the descriptions. */
function useReferenceNames(catalogs: AutomationCatalogs, needed: () => string[]) {
  return toSignal(
    toObservable(computed(needed)).pipe(
      switchMap((list) =>
        list.length === 0
          ? of(new Map<string, string>())
          : combineLatest(list.map((c) => catalogs.lookupNames(c).pipe(catchError(() => of([] as Lookup[]))))).pipe(
              map((lists) => new Map(lists.flat().map((l) => [l.id, l.name] as [string, string]))),
            ),
      ),
    ),
    { initialValue: new Map<string, string>() },
  );
}

// ---- List ---------------------------------------------------------------------------------------------------

/**
 * Flujos de autorización: the company's own and the global ones (the platform administrator defines them for every
 * company; an optional global workflow can be switched off for the company).
 */
@Component({
  selector: 'br-workflows',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, MatTooltipModule, TPipe, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.workflows' | t" icon="account_tree" [subtitle]="'workflows.hint' | t">
        <a mat-stroked-button [routerLink]="nav.to('autorizaciones')"><mat-icon>approval</mat-icon>{{ 'nav.approvals' | t }}</a>
        @if (canManage()) {
          <a mat-flat-button [routerLink]="nav.to('configuracion/flujos/nuevo')"><mat-icon>add</mat-icon>{{ 'workflows.new' | t }}</a>
        }
      </br-page-header>
      @if (definitions.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <div class="cl-table-wrapper br-table-wrapper">
        <table class="br-table">
          <thead>
            <tr>
              <th>{{ 'automation.name' | t }}</th>
              <th>{{ 'automation.scope.title' | t }}</th>
              <th class="br-wide">{{ 'automation.conditions' | t }}</th>
              <th class="br-wide">{{ 'workflows.steps' | t }}</th>
              <th class="br-end">{{ 'workflows.pending' | t }}</th>
              <th class="cl-actions-cell"></th>
            </tr>
          </thead>
          <tbody>
            @for (d of definitions.value() ?? []; track d.id) {
              <tr [class.br-row-muted]="!d.isEnabled || d.optedOut">
                <td>
                  <a class="cl-row-link" [routerLink]="nav.to('configuracion/flujos', d.id)">{{ d.name }}</a>
                  <div class="br-sub"><mat-icon class="br-inline-icon">{{ documentOf(d)?.icon }}</mat-icon>{{ catalogs.documentName(catalog(), d.document) }}</div>
                </td>
                <td>
                  @if (d.isGlobal) {
                    <span class="cl-chip cl-chip--info">{{ (d.isMandatory ? 'automation.scope.globalMandatory' : 'automation.scope.globalOptional') | t }}</span>
                  } @else {
                    <span class="cl-chip">{{ 'automation.scope.company' | t }}</span>
                  }
                  @if (!d.isEnabled) {
                    <div class="br-sub">{{ 'automation.disabled' | t }}</div>
                  } @else if (d.optedOut) {
                    <div class="br-sub">{{ 'automation.optedOut' | t }}</div>
                  }
                </td>
                <td class="br-wide">{{ catalogs.describeAll(documentOf(d), d.conditions, d.match, names()) }}</td>
                <td class="br-wide">{{ stepsOf(d) }}</td>
                <td class="br-end">{{ d.pending }}</td>
                <td class="cl-actions-cell br-nowrap">
                  @if (d.isGlobal && !d.isMandatory && canManage()) {
                    <mat-slide-toggle [checked]="!d.optedOut" (change)="optOut(d, !$event.checked)" [matTooltip]="'automation.optOutHint' | t">{{ 'automation.activeHere' | t }}</mat-slide-toggle>
                  }
                </td>
              </tr>
            } @empty {
              @if (!definitions.isLoading()) {
                <tr><td colspan="6" class="br-table-empty">{{ 'workflows.empty' | t }}</td></tr>
              }
            }
          </tbody>
        </table>
      </div>
      <p class="cl-muted br-hint-line"><mat-icon class="br-inline-icon">info</mat-icon>{{ 'workflows.order' | t }}</p>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkflowsPage {
  private readonly api = inject(BrocalApi);
  private readonly session = inject(BrocalSession);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly catalogs = inject(AutomationCatalogs);
  protected readonly nav = inject(BrocalNav);

  private readonly reload = signal(0);
  protected readonly canManage = computed(() => this.session.can(P.automationManage));
  protected readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });
  private readonly roles = apiResource({ stream: () => this.api.get<Role[]>('company/roles') });

  protected readonly definitions = apiResource({
    params: () => this.reload(),
    stream: () => this.api.get<WorkflowDefinition[]>('automation/workflows'),
  });

  protected readonly names = useReferenceNames(this.catalogs, () =>
    [...new Set((this.definitions.value() ?? []).flatMap((d) => this.catalogs.referenceCatalogs(this.documentOf(d), d.conditions)))],
  );

  protected documentOf(definition: WorkflowDefinition) {
    return this.catalog()?.documents.find((d) => d.document === definition.document);
  }

  protected stepsOf(definition: WorkflowDefinition): string {
    return definition.steps.map((s) => `${s.name} (${this.catalogs.describeAudience(s.approvers, this.catalog(), this.roles.value())})`).join(' → ');
  }

  protected async optOut(definition: WorkflowDefinition, optOut: boolean): Promise<void> {
    try {
      await firstValueFrom(this.api.put(`automation/workflows/${definition.id}/opt-out`, { optOut }));
      this.dialogs.success(this.i18n.t(optOut ? 'automation.optedOutOk' : 'automation.optedInOk'));
    } catch {
      // Shown by the HTTP error handler.
    }

    this.reload.update((r) => r + 1);
  }
}

// ---- Editor -------------------------------------------------------------------------------------------------

/**
 * Editor of a workflow: scope (this company or every company, for the platform administrator), document,
 * conditions that make it apply and the approval steps. Global definitions are read only for the companies.
 */
@Component({
  selector: 'br-workflow-editor',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatRadioModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTooltipModule,
    TPipe,
    PageHeader,
    ConditionsEditor,
    AudienceEditor,
  ],
  template: `
    <div class="cl-page">
      <br-page-header [title]="title()" icon="account_tree" [subtitle]="'workflows.editorHint' | t">
        <a mat-button [routerLink]="nav.to('configuracion/flujos')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
        @if (definition()?.canEdit) {
          <button mat-stroked-button type="button" (click)="remove()" [disabled]="busy()"><mat-icon>delete</mat-icon>{{ 'common.delete' | t }}</button>
        }
      </br-page-header>
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (definition(); as d) {
        @if (!d.canEdit) {
          <div class="br-warning-box br-warning-box--info">
            <mat-icon>public</mat-icon>
            <span>{{ (d.isGlobal ? (d.isMandatory ? 'automation.globalMandatoryReadOnly' : 'automation.globalOptionalReadOnly') : 'automation.readOnly') | t }}</span>
          </div>
        }
        @if (d.pending > 0) {
          <div class="br-warning-box br-warning-box--info"><mat-icon>pending_actions</mat-icon>{{ 'workflows.pendingWarning' | t: { n: d.pending } }}</div>
        }
      }

      <form [formGroup]="form" class="br-editor">
        <section class="br-card">
          <h2>{{ 'automation.general' | t }}</h2>
          @if (isNew() && catalog()?.canManageGlobal) {
            <mat-radio-group formControlName="global" class="br-scope-choice">
              <mat-radio-button [value]="false">{{ 'automation.scope.companyLong' | t }}</mat-radio-button>
              <mat-radio-button [value]="true">{{ 'automation.scope.globalLong' | t }}</mat-radio-button>
            </mat-radio-group>
          } @else if (definition(); as d) {
            <p>
              @if (d.isGlobal) {
                <span class="cl-chip cl-chip--info">{{ (d.isMandatory ? 'automation.scope.globalMandatory' : 'automation.scope.globalOptional') | t }}</span>
              } @else {
                <span class="cl-chip">{{ 'automation.scope.company' | t }}</span>
              }
            </p>
          }
          <div class="cl-form-grid">
            <mat-form-field>
              <mat-label>{{ 'automation.document' | t }}</mat-label>
              <mat-select formControlName="document">
                @for (doc of documents(); track doc.document) {
                  <mat-option [value]="doc.document"><mat-icon>{{ doc.icon }}</mat-icon>{{ catalogs.name(doc) }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'automation.name' | t }}</mat-label>
              <input matInput formControlName="name" maxlength="120" />
              <mat-error>{{ 'validation.required' | t }}</mat-error>
            </mat-form-field>
            <mat-form-field class="br-span-2">
              <mat-label>{{ 'fields.description' | t }}</mat-label>
              <textarea matInput formControlName="description" rows="2" maxlength="500"></textarea>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'automation.priority' | t }}</mat-label>
              <input matInput type="number" formControlName="priority" min="0" max="999" />
              <mat-hint>{{ 'automation.priorityHint' | t }}</mat-hint>
            </mat-form-field>
            <div class="br-toggles">
              <mat-slide-toggle formControlName="isEnabled">{{ 'automation.enabled' | t }}</mat-slide-toggle>
              @if (global()) {
                <mat-checkbox formControlName="isMandatory">{{ 'automation.mandatory' | t }}</mat-checkbox>
              }
            </div>
          </div>
          @if (documentInfo()?.workflow; as gate) {
            <p class="cl-muted br-hint-line"><mat-icon class="br-inline-icon">info</mat-icon>{{ catalogs.name({ name: gate.description, nameEn: gate.descriptionEn }) }}</p>
          }
        </section>

        <section class="br-card">
          <div class="br-card__header">
            <h2>{{ 'automation.conditions' | t }}</h2>
            <mat-button-toggle-group formControlName="match" [hideSingleSelectionIndicator]="true">
              <mat-button-toggle value="all">{{ 'automation.matchAll' | t }}</mat-button-toggle>
              <mat-button-toggle value="any">{{ 'automation.matchAny' | t }}</mat-button-toggle>
            </mat-button-toggle-group>
          </div>
          <p class="cl-muted">{{ 'workflows.conditionsHint' | t }}</p>
          <br-conditions-editor [conditions]="form.controls.conditions" [document]="documentInfo()" [global]="global()" [match]="form.controls.match.value" [readonly]="readonly()" emptyText="workflows.noConditions" />
        </section>

        <section class="br-card">
          <h2>{{ 'workflows.steps' | t }}</h2>
          <p class="cl-muted">{{ 'workflows.stepsHint' | t }}</p>
          @for (step of form.controls.steps.controls; track step; let i = $index; let last = $last) {
            <div class="br-step-editor" [formGroup]="step">
              <div class="br-step-editor__header">
                <span class="br-step-editor__number">{{ i + 1 }}</span>
                <mat-form-field subscriptSizing="dynamic">
                  <mat-label>{{ 'workflows.stepName' | t }}</mat-label>
                  <input matInput formControlName="name" maxlength="80" />
                </mat-form-field>
                @if (!readonly()) {
                  <span class="br-step-editor__tools">
                    <button mat-icon-button type="button" (click)="move(i, -1)" [disabled]="i === 0" [matTooltip]="'workflows.moveUp' | t"><mat-icon>arrow_upward</mat-icon></button>
                    <button mat-icon-button type="button" (click)="move(i, 1)" [disabled]="last" [matTooltip]="'workflows.moveDown' | t"><mat-icon>arrow_downward</mat-icon></button>
                    <button mat-icon-button type="button" (click)="removeStep(i)" [disabled]="form.controls.steps.length === 1" [matTooltip]="'common.remove' | t"><mat-icon>delete</mat-icon></button>
                  </span>
                }
              </div>
              <h3 class="br-form-section">{{ 'workflows.approvers' | t }}</h3>
              <br-audience-editor [group]="step.controls.approvers" [global]="global()" [catalog]="catalog()" [roles]="roles.value() ?? []" [modules]="modules.value() ?? []" />
              <div class="cl-form-grid br-step-editor__options">
                <mat-form-field subscriptSizing="dynamic">
                  <mat-label>{{ 'workflows.requiredApprovals' | t }}</mat-label>
                  <input matInput type="number" formControlName="requiredApprovals" min="1" max="5" />
                </mat-form-field>
                <mat-form-field subscriptSizing="dynamic">
                  <mat-label>{{ 'workflows.slaHours' | t }}</mat-label>
                  <input matInput type="number" formControlName="slaHours" min="1" max="720" />
                  <mat-hint>{{ 'workflows.slaHint' | t }}</mat-hint>
                </mat-form-field>
                <mat-checkbox formControlName="allowRequester">{{ 'workflows.allowRequester' | t }}</mat-checkbox>
              </div>
            </div>
          }
          @if (!readonly()) {
            <button mat-stroked-button type="button" (click)="addStep()" [disabled]="form.controls.steps.length >= 6"><mat-icon>add</mat-icon>{{ 'workflows.addStep' | t }}</button>
          }
        </section>

        @if (!readonly()) {
          <div class="br-actions br-form-actions">
            <span class="cl-spacer"></span>
            <a mat-button [routerLink]="nav.to('configuracion/flujos')">{{ 'common.cancel' | t }}</a>
            <button mat-flat-button type="button" (click)="save()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
          </div>
        }
      </form>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class WorkflowEditorPage implements OnInit {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  protected readonly catalogs = inject(AutomationCatalogs);
  protected readonly nav = inject(BrocalNav);

  /** Route parameter (absent for a new workflow). */
  readonly id = input<string | undefined>(undefined);

  protected readonly busy = signal(false);
  protected readonly definition = signal<WorkflowDefinition | null>(null);
  protected readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });
  protected readonly roles = apiResource({ stream: () => this.api.get<Role[]>('company/roles') });
  protected readonly modules = apiResource({ stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }) });

  protected readonly form = new FormGroup({
    global: new FormControl(false, { nonNullable: true }),
    document: new FormControl<string | null>(null, Validators.required),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(120)] }),
    description: new FormControl<string | null>(null, Validators.maxLength(500)),
    isEnabled: new FormControl(true, { nonNullable: true }),
    isMandatory: new FormControl(false, { nonNullable: true }),
    priority: new FormControl(0, { nonNullable: true, validators: [Validators.min(0), Validators.max(999)] }),
    match: new FormControl<ConditionMatch>('all', { nonNullable: true }),
    conditions: new FormArray<ConditionForm>([]),
    steps: new FormArray<StepForm>([stepForm(null)]),
  });

  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  protected readonly isNew = computed(() => !this.id());
  protected readonly global = computed(() => this.definition()?.isGlobal ?? !!this.formValue().global);
  protected readonly readonly = computed(() => !!this.definition() && !this.definition()!.canEdit || !this.session.can(P.automationManage));
  protected readonly documents = computed(() => (this.catalog()?.documents ?? []).filter((d) => d.workflow && (d.enabled || this.global())));
  protected readonly documentInfo = computed(() => this.catalog()?.documents.find((d) => d.document === this.formValue().document));
  protected readonly title = computed(() => this.definition()?.name ?? this.i18n.t('workflows.new'));

  async ngOnInit(): Promise<void> {
    const id = this.id();
    if (!id) {
      return;
    }

    this.busy.set(true);
    try {
      const definitions = await firstValueFrom(this.api.get<WorkflowDefinition[]>('automation/workflows'));
      const definition = definitions.find((d) => d.id === id) ?? null;
      this.definition.set(definition);
      if (definition) {
        const catalog = await firstValueFrom(this.catalogs.catalog());
        const document = catalog.documents.find((d) => d.document === definition.document);
        this.form.patchValue({
          global: definition.isGlobal,
          document: definition.document,
          name: definition.name,
          description: definition.description,
          isEnabled: definition.isEnabled,
          isMandatory: definition.isMandatory,
          priority: definition.priority,
          match: definition.match,
        });
        this.form.setControl('conditions', new FormArray(definition.conditions.map((c) => conditionForm(c, this.catalogs.fact(document, c.field)))));
        this.form.setControl('steps', new FormArray(definition.steps.map((s) => stepForm(s))));
        if (this.readonly()) {
          this.form.disable();
        }
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected addStep(): void {
    this.form.controls.steps.push(stepForm(null));
  }

  protected removeStep(index: number): void {
    this.form.controls.steps.removeAt(index);
  }

  protected move(index: number, delta: number): void {
    const steps = this.form.controls.steps;
    const step = steps.at(index);
    steps.removeAt(index);
    steps.insert(index + delta, step);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.dialogs.error(new Error(this.i18n.t('automation.completeForm')));
      return;
    }

    const value = this.form.getRawValue();
    const global = this.global();
    const document = this.documentInfo();
    const steps: WorkflowStep[] = this.form.controls.steps.controls.map((s) => ({
      name: s.controls.name.value.trim(),
      approvers: readAudience(s.controls.approvers, global),
      requiredApprovals: Number(s.controls.requiredApprovals.value),
      slaHours: s.controls.slaHours.value ? Number(s.controls.slaHours.value) : null,
      allowRequester: s.controls.allowRequester.value,
    }));
    if (steps.some((s) => audienceIsEmpty(s.approvers))) {
      this.dialogs.error(new Error(this.i18n.t('workflows.approversRequired')));
      return;
    }

    const body = {
      global,
      name: value.name.trim(),
      description: value.description,
      document: value.document,
      isEnabled: value.isEnabled,
      isMandatory: global && value.isMandatory,
      priority: Number(value.priority),
      match: value.match,
      conditions: this.form.controls.conditions.controls.map((c) => readCondition(c, this.catalogs.fact(document, c.controls.field.value ?? ''))),
      steps,
    };

    this.busy.set(true);
    try {
      const id = this.id();
      await firstValueFrom(id ? this.api.put(`automation/workflows/${id}`, body) : this.api.post('automation/workflows', body));
      this.dialogs.success(this.i18n.t('common.saved'));
      await this.nav.navigate(['configuracion/flujos']);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const definition = this.definition();
    if (!definition || !(await this.dialogs.confirm(this.i18n.t('workflows.deleteTitle'), this.i18n.t('workflows.deleteMessage', { name: definition.name }), this.i18n.t('common.delete'), true))) {
      return;
    }

    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(`automation/workflows/${definition.id}`));
      this.dialogs.success(this.i18n.t('common.deleted'));
      await this.nav.navigate(['configuracion/flujos']);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
