import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';
import { CompanySettings, ModuleInfo } from '../../core/models';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { EntityForm } from '../../shared/crud/entity-form';
import { FieldDef, FieldOption, buildForm, readForm } from '../../shared/crud/fields';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader } from '../../shared/ui';
import { TAX_REGIMES } from '../catalogs/catalog-configs';
import { BrandingEditor, BrandingPayload } from './branding-editor';

const TIME_ZONES: FieldOption[] = [
  { value: 'America/Mexico_City', label: 'company.timeZones.mexicoCity' },
  { value: 'America/Monterrey', label: 'company.timeZones.monterrey' },
  { value: 'America/Merida', label: 'company.timeZones.merida' },
  { value: 'America/Cancun', label: 'company.timeZones.cancun' },
  { value: 'America/Chihuahua', label: 'company.timeZones.chihuahua' },
  { value: 'America/Mazatlan', label: 'company.timeZones.mazatlan' },
  { value: 'America/Tijuana', label: 'company.timeZones.tijuana' },
];

export const SETTINGS_FIELDS: FieldDef[] = [
  { key: 'timeZone', label: 'company.timeZone', type: 'select', options: TIME_ZONES, required: true },
  { key: 'currency', label: 'fields.currency', type: 'select', options: [{ value: 'MXN', label: 'MXN', literal: true }, { value: 'USD', label: 'USD', literal: true }], required: true },
  { key: 'defaultLanguage', label: 'company.language', type: 'select', options: [{ value: 'es', label: 'Español', literal: true }, { value: 'en', label: 'English', literal: true }], required: true },
  { key: 'defaultVatRate', label: 'fields.vatRate', type: 'percent', required: true, min: 0, max: 100 },
  { key: 'tickets', label: 'company.ticketsSection', type: 'section' },
  { key: 'minTicketPhotos', label: 'company.minTicketPhotos', type: 'number', min: 0, max: 30, required: true },
  { key: 'requireCustomerSignature', label: 'company.requireCustomerSignature', type: 'checkbox' },
  { key: 'requireApprovalBeforeInvoice', label: 'company.requireApproval', type: 'checkbox' },
  { key: 'uninvoicedAlertDays', label: 'company.uninvoicedAlertDays', type: 'number', min: 1, required: true, hint: 'company.uninvoicedAlertHint' },
  { key: 'expiryWarningDays', label: 'company.expiryWarningDays', type: 'number', min: 1, required: true, hint: 'company.expiryWarningHint' },
  { key: 'fuelEfficiencyTolerancePercent', label: 'company.fuelTolerance', type: 'number', min: 1, max: 100, required: true, hint: 'company.fuelToleranceHint' },
  { key: 'folios', label: 'company.foliosSection', type: 'section' },
  { key: 'ticketPrefix', label: 'company.prefixes.ticket', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'workOrderPrefix', label: 'company.prefixes.workOrder', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'quotePrefix', label: 'company.prefixes.quote', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'maintenancePrefix', label: 'company.prefixes.maintenance', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'incidentPrefix', label: 'company.prefixes.incident', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'tripPrefix', label: 'company.prefixes.trip', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'estimatePrefix', label: 'company.prefixes.estimate', type: 'text', required: true, upper: true, maxLength: 10 },
  { key: 'expensePrefix', label: 'company.prefixes.expense', type: 'text', required: true, upper: true, maxLength: 10 },
];

function fiscalFields(providers: string[]): FieldDef[] {
  return [
    { key: 'rfc', label: 'fields.rfc', type: 'text', upper: true, maxLength: 13, required: true },
    { key: 'legalName', label: 'fields.legalName', type: 'text', required: true, hint: 'company.legalNameHint' },
    { key: 'taxRegime', label: 'fields.taxRegime', type: 'select', options: TAX_REGIMES, required: true },
    { key: 'postalCode', label: 'company.expeditionPostalCode', type: 'text', required: true, pattern: '^\\d{5}$' },
    { key: 'invoiceSeries', label: 'company.invoiceSeries', type: 'text', required: true, upper: true, maxLength: 10 },
    { key: 'paymentSeries', label: 'company.paymentSeries', type: 'text', required: true, upper: true, maxLength: 10 },
    { key: 'pac', label: 'company.pacSection', type: 'section' },
    { key: 'pacProvider', label: 'company.pacProvider', type: 'select', options: providers.map((p) => ({ value: p, label: p, literal: true })), required: true, hint: 'company.pacHint' },
    { key: 'pacUser', label: 'company.pacUser', type: 'text' },
    { key: 'pacSecret', label: 'company.pacSecret', type: 'text', hint: 'company.pacSecretHint' },
    { key: 'pacSandbox', label: 'company.pacSandbox', type: 'checkbox' },
  ];
}

/** Mi empresa: operation settings, look and feel, fiscal data (CFDI and PAC), usage against the quotas and contracted modules. */
@Component({
  selector: 'br-company-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatTabsModule, TPipe, EntityForm, PageHeader, BrandingEditor],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.company' | t" icon="business" [subtitle]="session.tenant()?.name ?? null" />
      @if (company.isLoading() || busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (company.value(); as c) {
        <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
          @if (session.can(p.accessSettings)) {
            <mat-tab [label]="'company.tabs.general' | t">
              <section class="br-card br-tabs-body">
                <br-entity-form [fields]="settingsFields" [form]="settingsForm()!" />
                <div class="br-actions" style="margin-top: 12px">
                  <span class="cl-spacer"></span>
                  <button mat-flat-button type="button" (click)="saveSettings()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
                </div>
              </section>
            </mat-tab>
            <mat-tab [label]="'company.tabs.branding' | t">
              <section class="br-card br-tabs-body">
                <br-branding-editor [branding]="c.branding" [livePreview]="true" [busy]="busy()" (save)="saveBranding($event)" />
              </section>
            </mat-tab>
          }
          @if (session.can(p.invoicingSettings)) {
            <mat-tab [label]="'company.tabs.fiscal' | t">
              <section class="br-card br-tabs-body">
                @if (!c.fiscal.isComplete) {
                  <div class="br-warning-box" style="margin-bottom: 12px"><mat-icon>warning</mat-icon>{{ 'company.fiscalIncomplete' | t }}</div>
                }
                <br-entity-form [fields]="fiscal()" [form]="fiscalForm()!" />
                <div class="br-actions" style="margin-top: 12px">
                  <span class="cl-muted">{{ c.fiscal.hasPacSecret ? ('company.pacSecretStored' | t) : '' }}</span>
                  <span class="cl-spacer"></span>
                  <button mat-flat-button type="button" (click)="saveFiscal()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
                </div>
              </section>
            </mat-tab>
          }
          <mat-tab [label]="'company.tabs.usage' | t">
            <section class="br-card br-tabs-body">
              <h2>{{ 'company.usage' | t }}</h2>
              <table class="br-table">
                <tbody>
                  @for (row of usage(); track row.key) {
                    <tr>
                      <td>{{ 'company.usageItems.' + row.key | t }}</td>
                      <td class="br-end">{{ row.used }}</td>
                      <td class="br-end cl-muted">{{ row.max === null ? ('company.unlimited' | t) : ('company.of' | t: { n: row.max }) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
              <h2 style="margin-top: 20px">{{ 'company.modules' | t }}</h2>
              <div class="br-modules">
                @for (module of modules.value() ?? []; track module.code) {
                  <div class="br-module" [class.br-module--off]="!c.enabledModules.includes(module.code)">
                    <mat-icon>{{ module.icon }}</mat-icon>
                    <div>
                      <strong>{{ module.number }}. {{ module.name }}</strong>
                      <small class="cl-muted">{{ c.enabledModules.includes(module.code) ? ('company.contracted' | t) : ('company.notContracted' | t) }}</small>
                    </div>
                  </div>
                }
              </div>
            </section>
          </mat-tab>
        </mat-tab-group>
      }
    </div>
  `,
  styles: `
    .br-modules { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 8px; }
    .br-module { display: flex; gap: 10px; align-items: center; padding: 10px; border-radius: var(--cl-radius-sm, 8px); background: var(--mat-sys-primary-container); color: var(--mat-sys-on-primary-container); }
    .br-module div { display: flex; flex-direction: column; }
    .br-module--off { background: var(--mat-sys-surface-container); color: var(--mat-sys-on-surface-variant); opacity: 0.7; }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class CompanyPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  protected readonly session = inject(BrocalSession);
  protected readonly p = P;
  protected readonly settingsFields = SETTINGS_FIELDS;
  protected readonly busy = signal(false);

  protected readonly company = apiResource({ stream: () => this.api.get<CompanySettings>('company/settings') });
  protected readonly modules = apiResource({ stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }) });

  protected readonly settingsForm = computed<FormGroup | null>(() => {
    const c = this.company.value();
    return c ? buildForm(SETTINGS_FIELDS, c.settings as unknown as Record<string, unknown>) : null;
  });
  protected readonly fiscal = computed(() => fiscalFields(this.company.value()?.pacProviders ?? ['Simulado']));
  protected readonly fiscalForm = computed<FormGroup | null>(() => {
    const c = this.company.value();
    return c ? buildForm(this.fiscal(), { ...c.fiscal, pacSecret: null } as unknown as Record<string, unknown>) : null;
  });

  protected readonly usage = computed(() => {
    const c = this.company.value();
    if (!c) {
      return [];
    }

    return [
      { key: 'users', used: c.usage.users, max: c.quotas.maxUsers },
      { key: 'portalUsers', used: c.usage.portalUsers, max: c.quotas.maxPortalUsers },
      { key: 'units', used: c.usage.units, max: c.quotas.maxUnits },
      { key: 'employees', used: c.usage.employees, max: c.quotas.maxEmployees },
      { key: 'storage', used: `${c.usage.storageMb.toFixed(1)} MB`, max: c.quotas.maxStorageMb },
      { key: 'tickets', used: c.usage.ticketsThisMonth, max: c.quotas.maxTicketsPerMonth },
      { key: 'apiKeys', used: c.usage.apiKeys, max: c.quotas.maxApiKeys },
    ];
  });

  protected async saveSettings(): Promise<void> {
    const form = this.settingsForm();
    if (!form || form.invalid) {
      form?.markAllAsTouched();
      return;
    }

    await this.run(() => firstValueFrom(this.api.put('company/settings', readForm(SETTINGS_FIELDS, form))));
  }

  protected async saveBranding(branding: BrandingPayload): Promise<void> {
    await this.run(() => firstValueFrom(this.api.put('company/branding', branding)));
  }

  protected async saveFiscal(): Promise<void> {
    const form = this.fiscalForm();
    if (!form || form.invalid) {
      form?.markAllAsTouched();
      return;
    }

    await this.run(() => firstValueFrom(this.api.put('company/fiscal', readForm(this.fiscal(), form))));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      await this.session.refresh();
      this.company.reload();
      this.dialogs.success(this.i18n.t('common.saved'));
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
