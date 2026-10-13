import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
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
import { BrocalApi } from '../../core/api';
import { AutomationCatalogs } from '../../core/automation';
import { I18n, TPipe } from '../../core/i18n';
import { AutomationTrigger, BusinessRule, ConditionMatch, Lookup, ModuleInfo, NotificationSeverity, Role, RuleEffect } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader } from '../../shared/ui';
import { AudienceEditor, ConditionForm, ConditionsEditor, audienceForm, conditionForm, readAudience, readCondition } from './automation-editors';

const SEVERITIES: NotificationSeverity[] = ['info', 'warning', 'critical'];

// ---- List ---------------------------------------------------------------------------------------------------

/**
 * Reglas de negocio: at the moments of each document, when the conditions match, the operation is blocked with
 * the message of the rule or the recipients are notified. Company rules and global ones (for every company).
 */
@Component({
  selector: 'br-rules',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatSlideToggleModule, MatTooltipModule, TPipe, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.businessRules' | t" icon="rule_settings" [subtitle]="'rules.hint' | t">
        @if (canManage()) {
          <a mat-flat-button [routerLink]="nav.to('configuracion/reglas/nueva')"><mat-icon>add</mat-icon>{{ 'rules.new' | t }}</a>
        }
      </br-page-header>
      @if (rules.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <div class="cl-table-wrapper br-table-wrapper">
        <table class="br-table">
          <thead>
            <tr>
              <th>{{ 'automation.name' | t }}</th>
              <th>{{ 'automation.scope.title' | t }}</th>
              <th class="br-wide">{{ 'rules.when' | t }}</th>
              <th class="br-wide">{{ 'automation.conditions' | t }}</th>
              <th>{{ 'rules.effect' | t }}</th>
              <th class="cl-actions-cell"></th>
            </tr>
          </thead>
          <tbody>
            @for (r of rules.value() ?? []; track r.id) {
              <tr [class.br-row-muted]="!r.isEnabled || r.optedOut">
                <td>
                  <a class="cl-row-link" [routerLink]="nav.to('configuracion/reglas', r.id)">{{ r.name }}</a>
                  <div class="br-sub"><mat-icon class="br-inline-icon">{{ documentOf(r)?.icon }}</mat-icon>{{ catalogs.documentName(catalog(), r.document) }}</div>
                </td>
                <td>
                  @if (r.isGlobal) {
                    <span class="cl-chip cl-chip--info">{{ (r.isMandatory ? 'automation.scope.globalMandatory' : 'automation.scope.globalOptional') | t }}</span>
                  } @else {
                    <span class="cl-chip">{{ 'automation.scope.company' | t }}</span>
                  }
                  @if (!r.isEnabled) {
                    <div class="br-sub">{{ 'automation.disabled' | t }}</div>
                  } @else if (r.optedOut) {
                    <div class="br-sub">{{ 'automation.optedOut' | t }}</div>
                  }
                </td>
                <td class="br-wide">{{ triggersOf(r) }}</td>
                <td class="br-wide">{{ catalogs.describeAll(documentOf(r), r.conditions, r.match, names()) }}</td>
                <td>
                  <span class="cl-chip" [class.cl-chip--error]="r.effect === 'block'" [class.cl-chip--warning]="r.effect === 'notify'">
                    {{ 'enums.ruleEffect.' + r.effect | t }}
                  </span>
                </td>
                <td class="cl-actions-cell br-nowrap">
                  @if (r.isGlobal && !r.isMandatory && canManage()) {
                    <mat-slide-toggle [checked]="!r.optedOut" (change)="optOut(r, !$event.checked)" [matTooltip]="'automation.optOutHint' | t">{{ 'automation.activeHere' | t }}</mat-slide-toggle>
                  }
                </td>
              </tr>
            } @empty {
              @if (!rules.isLoading()) {
                <tr><td colspan="6" class="br-table-empty">{{ 'rules.empty' | t }}</td></tr>
              }
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RulesPage {
  private readonly api = inject(BrocalApi);
  private readonly session = inject(BrocalSession);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly catalogs = inject(AutomationCatalogs);
  protected readonly nav = inject(BrocalNav);

  private readonly reload = signal(0);
  protected readonly canManage = computed(() => this.session.can(P.automationManage));
  protected readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });

  protected readonly rules = apiResource({
    params: () => this.reload(),
    stream: () => this.api.get<BusinessRule[]>('automation/rules'),
  });

  protected readonly names = toSignal(
    toObservable(computed(() => [...new Set((this.rules.value() ?? []).flatMap((r) => this.catalogs.referenceCatalogs(this.documentOf(r), r.conditions)))])).pipe(
      switchMap((list) =>
        list.length === 0
          ? of(new Map<string, string>())
          : combineLatest(list.map((c) => this.catalogs.lookupNames(c).pipe(catchError(() => of([] as Lookup[]))))).pipe(
              map((lists) => new Map(lists.flat().map((l) => [l.id, l.name] as [string, string]))),
            ),
      ),
    ),
    { initialValue: new Map<string, string>() },
  );

  protected documentOf(rule: BusinessRule) {
    return this.catalog()?.documents.find((d) => d.document === rule.document);
  }

  protected triggersOf(rule: BusinessRule): string {
    const triggers = this.catalog()?.triggers ?? [];
    return rule.triggers.map((t) => {
      const info = triggers.find((x) => x.trigger === t);
      return info ? this.catalogs.name(info) : t;
    }).join(', ');
  }

  protected async optOut(rule: BusinessRule, optOut: boolean): Promise<void> {
    try {
      await firstValueFrom(this.api.put(`automation/rules/${rule.id}/opt-out`, { optOut }));
      this.dialogs.success(this.i18n.t(optOut ? 'automation.optedOutOk' : 'automation.optedInOk'));
    } catch {
      // Shown by the HTTP error handler.
    }

    this.reload.update((r) => r + 1);
  }
}

// ---- Editor -------------------------------------------------------------------------------------------------

/** Editor of a business rule: scope, document and moments, conditions, effect, message and recipients. */
@Component({
  selector: 'br-rule-editor',
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
      <br-page-header [title]="title()" icon="rule_settings" [subtitle]="'rules.editorHint' | t">
        <a mat-button [routerLink]="nav.to('configuracion/reglas')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
        @if (rule()?.canEdit) {
          <button mat-stroked-button type="button" (click)="remove()" [disabled]="busy()"><mat-icon>delete</mat-icon>{{ 'common.delete' | t }}</button>
        }
      </br-page-header>
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (rule(); as r) {
        @if (!r.canEdit) {
          <div class="br-warning-box br-warning-box--info">
            <mat-icon>public</mat-icon>
            <span>{{ (r.isGlobal ? (r.isMandatory ? 'automation.globalMandatoryReadOnly' : 'automation.globalOptionalReadOnly') : 'automation.readOnly') | t }}</span>
          </div>
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
          } @else if (rule(); as r) {
            <p>
              @if (r.isGlobal) {
                <span class="cl-chip cl-chip--info">{{ (r.isMandatory ? 'automation.scope.globalMandatory' : 'automation.scope.globalOptional') | t }}</span>
              } @else {
                <span class="cl-chip">{{ 'automation.scope.company' | t }}</span>
              }
            </p>
          }
          <div class="cl-form-grid">
            <mat-form-field>
              <mat-label>{{ 'automation.document' | t }}</mat-label>
              <mat-select formControlName="document" (selectionChange)="documentChanged()">
                @for (doc of documents(); track doc.document) {
                  <mat-option [value]="doc.document"><mat-icon>{{ doc.icon }}</mat-icon>{{ catalogs.name(doc) }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'rules.when' | t }}</mat-label>
              <mat-select formControlName="triggers" multiple>
                @for (trigger of triggers(); track trigger.trigger) {
                  <mat-option [value]="trigger.trigger">{{ catalogs.name(trigger) }}</mat-option>
                }
              </mat-select>
              <mat-error>{{ 'validation.required' | t }}</mat-error>
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
            </mat-form-field>
            <div class="br-toggles">
              <mat-slide-toggle formControlName="isEnabled">{{ 'automation.enabled' | t }}</mat-slide-toggle>
              @if (global()) {
                <mat-checkbox formControlName="isMandatory">{{ 'automation.mandatory' | t }}</mat-checkbox>
              }
            </div>
          </div>
        </section>

        <section class="br-card">
          <div class="br-card__header">
            <h2>{{ 'automation.conditions' | t }}</h2>
            <mat-button-toggle-group formControlName="match" [hideSingleSelectionIndicator]="true">
              <mat-button-toggle value="all">{{ 'automation.matchAll' | t }}</mat-button-toggle>
              <mat-button-toggle value="any">{{ 'automation.matchAny' | t }}</mat-button-toggle>
            </mat-button-toggle-group>
          </div>
          <p class="cl-muted">{{ 'rules.conditionsHint' | t }}</p>
          <br-conditions-editor [conditions]="form.controls.conditions" [document]="documentInfo()" [global]="global()" [match]="form.controls.match.value" [readonly]="readonly()" emptyText="rules.noConditions" />
        </section>

        <section class="br-card">
          <h2>{{ 'rules.effect' | t }}</h2>
          <mat-radio-group formControlName="effect" class="br-scope-choice">
            <mat-radio-button value="block">{{ 'rules.effects.block' | t }}</mat-radio-button>
            <mat-radio-button value="notify">{{ 'rules.effects.notify' | t }}</mat-radio-button>
          </mat-radio-group>
          <div class="cl-form-grid">
            <mat-form-field class="br-span-2">
              <mat-label>{{ 'rules.message' | t }}</mat-label>
              <textarea matInput formControlName="message" rows="2" maxlength="500"></textarea>
              <mat-hint>{{ 'rules.messageHint' | t }}</mat-hint>
              <mat-error>{{ 'validation.required' | t }}</mat-error>
            </mat-form-field>
            <mat-form-field>
              <mat-label>{{ 'rules.severity' | t }}</mat-label>
              <mat-select formControlName="severity">
                @for (s of severities; track s) {
                  <mat-option [value]="s">{{ 'enums.notificationSeverity.' + s | t }}</mat-option>
                }
              </mat-select>
            </mat-form-field>
          </div>
          @if (placeholders().length > 0) {
            <div class="br-chips br-placeholders">
              @for (p of placeholders(); track p.key) {
                <button type="button" class="cl-chip" (click)="insert(p.key)" [matTooltip]="p.label" [disabled]="readonly()">{{ '{' + p.key + '}' }}</button>
              }
            </div>
          }
        </section>

        <section class="br-card">
          <h2>{{ (form.controls.effect.value === 'notify' ? 'rules.recipients' : 'rules.alsoNotify') | t }}</h2>
          <p class="cl-muted">{{ (form.controls.effect.value === 'notify' ? 'rules.recipientsHint' : 'rules.alsoNotifyHint') | t }}</p>
          <br-audience-editor [group]="form.controls.audience" [global]="global()" [catalog]="catalog()" [roles]="roles.value() ?? []" [modules]="modules.value() ?? []" />
          <div class="cl-form-grid">
            <mat-form-field class="br-span-2">
              <mat-label>{{ 'rules.emails' | t }}</mat-label>
              <input matInput formControlName="emails" />
              <mat-hint>{{ 'rules.emailsHint' | t }}</mat-hint>
            </mat-form-field>
            <mat-checkbox formControlName="notifyActor">{{ 'rules.notifyActor' | t }}</mat-checkbox>
          </div>
        </section>

        @if (!readonly()) {
          <div class="br-actions br-form-actions">
            <span class="cl-spacer"></span>
            <a mat-button [routerLink]="nav.to('configuracion/reglas')">{{ 'common.cancel' | t }}</a>
            <button mat-flat-button type="button" (click)="save()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
          </div>
        }
      </form>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class RuleEditorPage implements OnInit {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  protected readonly catalogs = inject(AutomationCatalogs);
  protected readonly nav = inject(BrocalNav);
  protected readonly severities = SEVERITIES;

  readonly id = input<string | undefined>(undefined);

  protected readonly busy = signal(false);
  protected readonly rule = signal<BusinessRule | null>(null);
  protected readonly catalog = toSignal(this.catalogs.catalog().pipe(catchError(() => of(null))), { initialValue: null });
  protected readonly roles = apiResource({ stream: () => this.api.get<Role[]>('company/roles') });
  protected readonly modules = apiResource({ stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }) });

  protected readonly form = new FormGroup({
    global: new FormControl(false, { nonNullable: true }),
    document: new FormControl<string | null>(null, Validators.required),
    triggers: new FormControl<AutomationTrigger[]>([], { nonNullable: true, validators: Validators.required }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(120)] }),
    description: new FormControl<string | null>(null, Validators.maxLength(500)),
    isEnabled: new FormControl(true, { nonNullable: true }),
    isMandatory: new FormControl(false, { nonNullable: true }),
    priority: new FormControl(0, { nonNullable: true, validators: [Validators.min(0), Validators.max(999)] }),
    match: new FormControl<ConditionMatch>('all', { nonNullable: true }),
    conditions: new FormArray<ConditionForm>([]),
    effect: new FormControl<RuleEffect>('block', { nonNullable: true }),
    message: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] }),
    severity: new FormControl<NotificationSeverity>('warning', { nonNullable: true }),
    audience: audienceForm(null),
    emails: new FormControl<string | null>(null),
    notifyActor: new FormControl(false, { nonNullable: true }),
  });

  private readonly formValue = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  protected readonly isNew = computed(() => !this.id());
  protected readonly global = computed(() => this.rule()?.isGlobal ?? !!this.formValue().global);
  protected readonly readonly = computed(() => !!this.rule() && !this.rule()!.canEdit || !this.session.can(P.automationManage));
  protected readonly documents = computed(() => (this.catalog()?.documents ?? []).filter((d) => d.enabled || this.global()));
  protected readonly documentInfo = computed(() => this.catalog()?.documents.find((d) => d.document === this.formValue().document));
  protected readonly triggers = computed(() => (this.catalog()?.triggers ?? []).filter((t) => this.documentInfo()?.triggers.includes(t.trigger)));
  protected readonly title = computed(() => this.rule()?.name ?? this.i18n.t('rules.new'));

  /** Values of the document the message can quote. */
  protected readonly placeholders = computed(() => [
    { key: 'folio', label: this.i18n.t('fields.folio') },
    ...(this.documentInfo()?.facts ?? []).filter((f) => f.type !== 'reference').map((f) => ({ key: f.key, label: this.catalogs.name(f) })),
  ]);

  async ngOnInit(): Promise<void> {
    const id = this.id();
    if (!id) {
      return;
    }

    this.busy.set(true);
    try {
      const rules = await firstValueFrom(this.api.get<BusinessRule[]>('automation/rules'));
      const rule = rules.find((r) => r.id === id) ?? null;
      this.rule.set(rule);
      if (rule) {
        const catalog = await firstValueFrom(this.catalogs.catalog());
        const document = catalog.documents.find((d) => d.document === rule.document);
        this.form.patchValue({
          global: rule.isGlobal,
          document: rule.document,
          triggers: rule.triggers,
          name: rule.name,
          description: rule.description,
          isEnabled: rule.isEnabled,
          isMandatory: rule.isMandatory,
          priority: rule.priority,
          match: rule.match,
          effect: rule.effect,
          message: rule.message,
          severity: rule.severity,
          emails: rule.recipients.emails.join(', ') || null,
          notifyActor: rule.recipients.notifyActor,
        });
        this.form.setControl('audience', audienceForm(rule.recipients.audience));
        this.form.setControl('conditions', new FormArray(rule.conditions.map((c) => conditionForm(c, this.catalogs.fact(document, c.field)))));
        if (this.readonly()) {
          this.form.disable();
        }
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** Another document has other moments and data: the moments that do not apply are dropped. */
  protected documentChanged(): void {
    const allowed = this.documentInfo()?.triggers ?? [];
    this.form.controls.triggers.setValue(this.form.controls.triggers.value.filter((t) => allowed.includes(t)));
    this.form.setControl('conditions', new FormArray<ConditionForm>([]));
  }

  protected insert(key: string): void {
    const control = this.form.controls.message;
    control.setValue(`${control.value}${control.value.endsWith(' ') || !control.value ? '' : ' '}{${key}}`);
  }

  protected async save(): Promise<void> {
    if (this.form.invalid || this.form.controls.conditions.length === 0) {
      this.form.markAllAsTouched();
      this.dialogs.error(new Error(this.i18n.t(this.form.controls.conditions.length === 0 ? 'rules.conditionsRequired' : 'automation.completeForm')));
      return;
    }

    const value = this.form.getRawValue();
    const global = this.global();
    const document = this.documentInfo();
    const body = {
      global,
      name: value.name.trim(),
      description: value.description,
      document: value.document,
      triggers: value.triggers,
      isEnabled: value.isEnabled,
      isMandatory: global && value.isMandatory,
      priority: Number(value.priority),
      match: value.match,
      conditions: this.form.controls.conditions.controls.map((c) => readCondition(c, this.catalogs.fact(document, c.controls.field.value ?? ''))),
      effect: value.effect,
      message: value.message.trim(),
      severity: value.severity,
      recipients: {
        audience: readAudience(this.form.controls.audience, global),
        emails: (value.emails ?? '').split(/[,;\s]+/).map((e) => e.trim()).filter(Boolean),
        notifyActor: value.notifyActor,
      },
    };

    this.busy.set(true);
    try {
      const id = this.id();
      await firstValueFrom(id ? this.api.put(`automation/rules/${id}`, body) : this.api.post('automation/rules', body));
      this.dialogs.success(this.i18n.t('common.saved'));
      await this.nav.navigate(['configuracion/reglas']);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const rule = this.rule();
    if (!rule || !(await this.dialogs.confirm(this.i18n.t('rules.deleteTitle'), this.i18n.t('rules.deleteMessage', { name: rule.name }), this.i18n.t('common.delete'), true))) {
      return;
    }

    this.busy.set(true);
    try {
      await firstValueFrom(this.api.delete(`automation/rules/${rule.id}`));
      this.dialogs.success(this.i18n.t('common.deleted'));
      await this.nav.navigate(['configuracion/reglas']);
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
