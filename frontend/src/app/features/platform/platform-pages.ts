import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { DatePipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { ModuleInfo, TenantDetail, TenantStatusAction, TenantSummary } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { EntityForm } from '../../shared/crud/entity-form';
import { FieldDef, buildForm, readForm } from '../../shared/crud/fields';
import { ListConfig } from '../../shared/crud/list-page';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';
import { BrandingEditor, BrandingPayload } from '../admin/branding-editor';

const PLANS = ['Básico', 'Profesional', 'Empresarial'].map((p) => ({ value: p, label: p, literal: true }));

const PROFILE_FIELDS: FieldDef[] = [
  { key: 'name', label: 'fields.name', type: 'text', required: true },
  { key: 'legalName', label: 'fields.legalName', type: 'text' },
  { key: 'taxId', label: 'fields.rfc', type: 'text', upper: true },
  { key: 'plan', label: 'tenants.plan', type: 'select', options: PLANS, required: true },
  { key: 'contactEmail', label: 'fields.email', type: 'text' },
  { key: 'contactPhone', label: 'fields.phone', type: 'text' },
];

const QUOTA_FIELDS: FieldDef[] = [
  { key: 'maxUsers', label: 'tenants.quotas.users', type: 'number', min: 0, hint: 'tenants.quotas.emptyUnlimited' },
  { key: 'maxPortalUsers', label: 'tenants.quotas.portalUsers', type: 'number', min: 0 },
  { key: 'maxUnits', label: 'tenants.quotas.units', type: 'number', min: 0 },
  { key: 'maxEmployees', label: 'tenants.quotas.employees', type: 'number', min: 0 },
  { key: 'maxStorageMb', label: 'tenants.quotas.storage', type: 'number', min: 0 },
  { key: 'maxTicketsPerMonth', label: 'tenants.quotas.tickets', type: 'number', min: 0 },
  { key: 'maxApiKeys', label: 'tenants.quotas.apiKeys', type: 'number', min: 0 },
];

/** Companies of the platform (platform administrators). */
export const TENANT_LIST: ListConfig<TenantSummary> = {
  key: 'tenants',
  icon: 'domain_add',
  api: 'platform/tenants',
  link: (r) => ['plataforma/empresas', r.id],
  query: {},
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'tenantStatus', values: ['trial', 'active', 'suspended', 'cancelled'] },
  ],
  headerActions: [
    {
      key: 'create',
      icon: 'add_business',
      label: 'tenants.new',
      primary: true,
      run: async (ctx) => {
        const modules = await firstValueFrom(ctx.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }));
        const created = (await ctx.form(
          {
            title: ctx.i18n.t('tenants.new'),
            width: '900px',
            fields: [
              { key: 'slug', label: 'tenants.slug', type: 'text', required: true, pattern: '^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$', hint: 'tenants.slugHint' },
              ...PROFILE_FIELDS,
              {
                key: 'modules',
                label: 'tenants.modules',
                type: 'multiselect',
                span: 'full',
                options: modules.filter((m) => !m.isCore).map((m) => ({ value: m.code, label: `${m.number}. ${m.name}`, literal: true })),
                hint: 'tenants.modulesHint',
              },
              { key: 'trialDays', label: 'tenants.trialDays', type: 'number', min: 0, hint: 'tenants.trialHint' },
              { key: 'admin', label: 'tenants.adminSection', type: 'section' },
              { key: 'adminEmail', label: 'fields.email', type: 'text', required: true },
              { key: 'adminName', label: 'fields.name', type: 'text', required: true },
              { key: 'provisionAdminAccount', label: 'tenants.provisionAccount', type: 'checkbox', span: 'full', hint: 'tenants.provisionHint' },
              { key: 'adminTemporaryPassword', label: 'members.temporaryPassword', type: 'text', showIf: (v) => v['provisionAdminAccount'] === true },
            ],
            save: (api, v) => api.post<{ id: string }>('platform/tenants', { ...v, quotas: null, provisionAdminAccount: !!v['provisionAdminAccount'] }, { tenant: false }),
          },
          { plan: 'Profesional', modules: modules.filter((m) => !m.isCore).map((m) => m.code), trialDays: 30, provisionAdminAccount: false },
        )) as { id: string } | null;
        if (created) {
          await ctx.session.refresh();
          await ctx.nav.navigate(['plataforma/empresas', created.id]);
        }

        return !!created;
      },
    },
  ],
  columns: [
    { key: 'name', label: 'fields.name', sub: (r) => r.slug },
    { key: 'primaryColor', label: 'fields.color', type: 'color', wide: true },
    { key: 'plan', label: 'tenants.plan' },
    { key: 'moduleCount', label: 'tenants.modules', type: 'number', align: 'end' },
    { key: 'users', label: 'tenants.users', type: 'number', align: 'end', wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'tenantStatus', sub: (r) => (r.trialEndsOn ? `prueba hasta ${r.trialEndsOn}` : null) },
    { key: 'createdAt', label: 'fields.createdAt', type: 'date', wide: true },
  ],
};

/** A company of the platform: profile, contracted modules (with dependencies), quotas, look and feel, status and usage. */
@Component({
  selector: 'br-tenant-detail',
  imports: [RouterLink, MatButtonModule, MatCheckboxModule, MatIconModule, MatProgressBarModule, MatTabsModule, MatTooltipModule, TPipe, DatePipe, EntityForm, PageHeader, StatusChip, BrandingEditor],
  template: `
    <div class="cl-page">
      @if (tenant.isLoading() || busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @if (tenant.value(); as t) {
        <br-page-header [title]="t.name" icon="domain" [subtitle]="t.slug + ' · ' + t.plan">
          <a mat-button [routerLink]="nav.to('plataforma/empresas')"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
          @for (action of statusActions(); track action) {
            <button mat-stroked-button type="button" (click)="changeStatus(action)"><mat-icon>{{ icons[action] }}</mat-icon>{{ 'tenants.actions.' + action | t }}</button>
          }
        </br-page-header>
        <div class="br-actions">
          <br-status enum="tenantStatus" [value]="t.status" />
          @if (t.statusReason) {
            <span class="cl-muted">{{ t.statusReason }}</span>
          }
          @if (t.trialEndsOn) {
            <span class="cl-chip cl-chip--info">{{ 'tenants.trialUntil' | t: { date: (t.trialEndsOn | d) } }}</span>
          }
        </div>
        <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
          <mat-tab [label]="'tenants.tabs.profile' | t">
            <section class="br-card br-tabs-body">
              <br-entity-form [fields]="profileFields" [form]="profileForm()!" />
              <div class="br-actions" style="margin-top: 12px"><span class="cl-spacer"></span><button mat-flat-button type="button" (click)="saveProfile()">{{ 'common.save' | t }}</button></div>
            </section>
          </mat-tab>
          <mat-tab [label]="'tenants.tabs.modules' | t">
            <section class="br-card br-tabs-body">
              <p class="cl-muted">{{ 'tenants.modulesHint' | t }}</p>
              @for (category of categories(); track category.key) {
                <h3 class="br-form-section">{{ 'nav.groups.' + category.key | t }}</h3>
                <div class="br-module-list">
                  @for (module of category.modules; track module.code) {
                    <mat-checkbox [checked]="selectedModules().has(module.code)" [disabled]="module.isCore" (change)="toggleModule(module, $event.checked)" [matTooltip]="module.description">
                      <mat-icon class="br-inline-icon">{{ module.icon }}</mat-icon>{{ module.number }}. {{ module.name }}
                      @if (module.dependsOn.length > 0) {
                        <small class="cl-muted">({{ 'tenants.requires' | t }} {{ module.dependsOn.join(', ') }})</small>
                      }
                    </mat-checkbox>
                  }
                </div>
              }
              <div class="br-actions" style="margin-top: 12px"><span class="cl-spacer"></span><button mat-flat-button type="button" (click)="saveModules()">{{ 'common.save' | t }}</button></div>
            </section>
          </mat-tab>
          <mat-tab [label]="'tenants.tabs.quotas' | t">
            <section class="br-card br-tabs-body">
              <br-entity-form [fields]="quotaFields" [form]="quotaForm()!" />
              <h3 class="br-form-section">{{ 'company.usage' | t }}</h3>
              <p class="cl-muted">
                {{ 'company.usageItems.users' | t }}: {{ t.usage.users }} · {{ 'company.usageItems.units' | t }}: {{ t.usage.units }} · {{ 'company.usageItems.employees' | t }}: {{ t.usage.employees }} ·
                {{ 'company.usageItems.storage' | t }}: {{ t.usage.storageMb.toFixed(1) }} MB · {{ 'company.usageItems.tickets' | t }}: {{ t.usage.ticketsThisMonth }}
              </p>
              <div class="br-actions" style="margin-top: 12px"><span class="cl-spacer"></span><button mat-flat-button type="button" (click)="saveQuotas()">{{ 'common.save' | t }}</button></div>
            </section>
          </mat-tab>
          <mat-tab [label]="'tenants.tabs.branding' | t">
            <section class="br-card br-tabs-body">
              <br-branding-editor [branding]="t.branding" [busy]="busy()" (save)="saveBranding($event)" />
            </section>
          </mat-tab>
        </mat-tab-group>
      }
    </div>
  `,
  styles: `
    .br-module-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 4px 16px; }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class TenantDetailPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);
  protected readonly profileFields = PROFILE_FIELDS;
  protected readonly quotaFields = QUOTA_FIELDS;
  protected readonly icons: Record<TenantStatusAction, string> = { activate: 'check_circle', startTrial: 'schedule', suspend: 'pause_circle', cancel: 'cancel' };

  readonly id = input.required<string>();
  protected readonly busy = signal(false);
  protected readonly selectedModules = signal(new Set<string>());

  protected readonly tenant = apiResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.get<TenantDetail>(`platform/tenants/${params}`, undefined, { tenant: false }),
  });
  private readonly modules = apiResource({ stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }) });

  protected readonly profileForm = computed<FormGroup | null>(() => {
    const t = this.tenant.value();
    return t ? buildForm(PROFILE_FIELDS, t as unknown as Record<string, unknown>) : null;
  });

  constructor() {
    effect(() => {
      const t = this.tenant.value();
      if (t) {
        this.selectedModules.set(new Set(t.enabledModules));
      }
    });
  }
  protected readonly quotaForm = computed<FormGroup | null>(() => {
    const t = this.tenant.value();
    return t ? buildForm(QUOTA_FIELDS, t.quotas as unknown as Record<string, unknown>) : null;
  });
  protected readonly categories = computed(() => {
    const modules = this.modules.value() ?? [];
    return ['operations', 'assets', 'compliance', 'fiscal', 'management', 'platform'].map((key) => ({ key, modules: modules.filter((m) => m.category === key) }));
  });

  protected readonly statusActions = computed<TenantStatusAction[]>(() => {
    const status = this.tenant.value()?.status;
    switch (status) {
      case 'trial':
        return ['activate', 'suspend', 'cancel'];
      case 'active':
        return ['suspend', 'cancel'];
      case 'suspended':
        return ['activate', 'cancel'];
      case 'cancelled':
        return ['activate'];
      default:
        return [];
    }
  });

  protected toggleModule(module: ModuleInfo, checked: boolean): void {
    const modules = this.modules.value() ?? [];
    this.selectedModules.update((set) => {
      const next = new Set(set);
      if (checked) {
        // Enabling a module enables its dependencies (the API does the same).
        const pending = [module.code];
        while (pending.length > 0) {
          const code = pending.pop()!;
          if (!next.has(code)) {
            next.add(code);
            pending.push(...(modules.find((m) => m.code === code)?.dependsOn ?? []));
          }
        }
      } else {
        next.delete(module.code);
      }

      return next;
    });
  }

  protected async saveProfile(): Promise<void> {
    const form = this.profileForm();
    if (form?.valid) {
      await this.run(() => firstValueFrom(this.api.put(`platform/tenants/${this.id()}`, readForm(PROFILE_FIELDS, form), { tenant: false })));
    }
  }

  protected async saveModules(): Promise<void> {
    await this.run(() => firstValueFrom(this.api.put(`platform/tenants/${this.id()}/modules`, { modules: [...this.selectedModules()] }, { tenant: false })));
  }

  protected async saveQuotas(): Promise<void> {
    const form = this.quotaForm();
    if (form?.valid) {
      await this.run(() => firstValueFrom(this.api.put(`platform/tenants/${this.id()}/quotas`, readForm(QUOTA_FIELDS, form), { tenant: false })));
    }
  }

  protected async saveBranding(branding: BrandingPayload): Promise<void> {
    await this.run(() => firstValueFrom(this.api.put(`platform/tenants/${this.id()}/branding`, branding, { tenant: false })));
  }

  protected async changeStatus(action: TenantStatusAction): Promise<void> {
    let reason: string | null = null;
    if (action === 'suspend' || action === 'cancel') {
      reason = await this.dialogs.prompt(this.i18n.t(`tenants.actions.${action}`), this.i18n.t('fields.reason'));
      if (!reason) {
        return;
      }
    }

    await this.run(() => firstValueFrom(this.api.put(`platform/tenants/${this.id()}/status`, { action, reason, trialEndsOn: null }, { tenant: false })));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.dialogs.success(this.i18n.t('common.saved'));
      this.tenant.reload();
      await this.session.refresh();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
