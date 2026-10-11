import { ChangeDetectionStrategy, Component, Injector, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, saveFile } from '../../core/api';
import { API } from '../../core/config';
import { DateTimePipe, firstOfMonth, today } from '../../core/format';
import { EnumPipe, I18n, TPipe } from '../../core/i18n';
import { AccountingSettings, ApiKeyInfo, ChannelSettings, CreatedApiKey, ExportDataset, NotificationRule, OutboxMessage, PagedResult, Role } from '../../core/models';
import { apiResource } from '../../core/resource';
import { EntityForm } from '../../shared/crud/entity-form';
import { FieldDef, buildForm, readForm } from '../../shared/crud/fields';
import { actionContext } from '../../shared/crud/list-page';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';

const EXPIRY_EVENTS = new Set(['permitExpiring', 'documentExpiring', 'maintenanceDue', 'invoiceOverdue', 'uninvoicedTickets']);

const CHANNEL_FIELDS: FieldDef[] = [
  { key: 'email', label: 'notifications.emailSection', type: 'section' },
  { key: 'useOwnSmtp', label: 'notifications.useOwnSmtp', type: 'checkbox', span: 'full', hint: 'notifications.useOwnSmtpHint' },
  { key: 'smtpHost', label: 'notifications.smtpHost', type: 'text', showIf: (v) => v['useOwnSmtp'] === true },
  { key: 'smtpPort', label: 'notifications.smtpPort', type: 'number', showIf: (v) => v['useOwnSmtp'] === true },
  { key: 'smtpUseSsl', label: 'notifications.smtpSsl', type: 'checkbox', showIf: (v) => v['useOwnSmtp'] === true },
  { key: 'smtpUser', label: 'notifications.smtpUser', type: 'text', showIf: (v) => v['useOwnSmtp'] === true },
  { key: 'smtpPassword', label: 'notifications.smtpPassword', type: 'text', showIf: (v) => v['useOwnSmtp'] === true, hint: 'notifications.secretHint' },
  { key: 'fromAddress', label: 'notifications.fromAddress', type: 'text' },
  { key: 'fromName', label: 'notifications.fromName', type: 'text' },
  { key: 'whatsapp', label: 'notifications.whatsAppSection', type: 'section' },
  { key: 'whatsAppEnabled', label: 'notifications.whatsAppEnabled', type: 'checkbox', span: 'full', hint: 'notifications.whatsAppHint' },
  { key: 'whatsAppPhoneNumberId', label: 'notifications.phoneNumberId', type: 'text', showIf: (v) => v['whatsAppEnabled'] === true },
  { key: 'whatsAppToken', label: 'notifications.token', type: 'text', showIf: (v) => v['whatsAppEnabled'] === true, hint: 'notifications.secretHint' },
  { key: 'whatsAppTemplate', label: 'notifications.template', type: 'text', required: true, showIf: (v) => v['whatsAppEnabled'] === true },
  { key: 'whatsAppLanguage', label: 'notifications.templateLanguage', type: 'text', required: true, showIf: (v) => v['whatsAppEnabled'] === true },
];

/** Notificaciones: who is notified of each event and by which channel (in-app, e-mail, WhatsApp), the company accounts and the delivery monitor. */
@Component({
  selector: 'br-notification-settings',
  imports: [FormsModule, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTabsModule, TPipe, EnumPipe, DateTimePipe, EntityForm, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.notificationSettings' | t" icon="notifications_active" [subtitle]="'notifications.hint' | t" />
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
        <mat-tab [label]="'notifications.tabs.rules' | t">
          <div class="cl-table-wrapper br-tabs-body">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'notifications.event' | t }}</th>
                  <th>{{ 'notifications.channels' | t }}</th>
                  <th class="br-wide">{{ 'notifications.recipients' | t }}</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (rule of rules.value() ?? []; track rule.event) {
                  <tr [class.br-row-muted]="!rule.isEnabled">
                    <td>{{ rule.event | enum: 'notificationEvent' }}@if (rule.isDefault) {<div class="br-sub">{{ 'notifications.default' | t }}</div>}</td>
                    <td>{{ channelsText(rule.channels) }}</td>
                    <td class="br-wide">{{ recipients(rule) }}</td>
                    <td class="cl-actions-cell"><button mat-icon-button type="button" (click)="editRule(rule)" [attr.aria-label]="'common.edit' | t"><mat-icon>edit</mat-icon></button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </mat-tab>
        <mat-tab [label]="'notifications.tabs.channels' | t">
          @if (channelForm(); as form) {
            <section class="br-card br-tabs-body">
              <br-entity-form [fields]="channelFields" [form]="form" />
              <div class="br-actions" style="margin-top: 12px">
                <span class="cl-muted">{{ secretsInfo() }}</span>
                <span class="cl-spacer"></span>
                <button mat-flat-button type="button" (click)="saveChannels(form)" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
              </div>
            </section>
          }
          <section class="br-card">
            <h2>{{ 'notifications.test' | t }}</h2>
            <div class="cl-toolbar-row">
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'fields.email' | t }}</mat-label>
                <input matInput [(ngModel)]="testEmail" />
              </mat-form-field>
              <mat-form-field subscriptSizing="dynamic">
                <mat-label>{{ 'fields.phoneWhatsApp' | t }}</mat-label>
                <input matInput [(ngModel)]="testPhone" />
              </mat-form-field>
              <button mat-stroked-button type="button" (click)="test()"><mat-icon>send</mat-icon>{{ 'notifications.sendTest' | t }}</button>
            </div>
          </section>
        </mat-tab>
        <mat-tab [label]="'notifications.tabs.outbox' | t">
          <div class="cl-table-wrapper br-tabs-body">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'notifications.createdAt' | t }}</th>
                  <th>{{ 'notifications.recipient' | t }}</th>
                  <th class="br-wide">{{ 'notifications.subject' | t }}</th>
                  <th>{{ 'fields.status' | t }}</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (m of outbox.value()?.items ?? []; track m.id) {
                  <tr>
                    <td>{{ m.createdAt | dt }}<div class="br-sub">{{ m.channel | enum: 'outboxChannel' }}</div></td>
                    <td>{{ m.recipient }}</td>
                    <td class="br-wide">{{ m.subject }}@if (m.lastError) {<div class="br-sub">{{ m.lastError }}</div>}</td>
                    <td><br-status enum="outboxStatus" [value]="m.status" /><div class="br-sub">{{ 'notifications.attempts' | t: { n: m.attempts } }}</div></td>
                    <td class="cl-actions-cell">
                      @if (m.status === 'failed') {
                        <button mat-icon-button type="button" (click)="retry(m)" [attr.aria-label]="'notifications.retry' | t"><mat-icon>replay</mat-icon></button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td colspan="5" class="br-table-empty">{{ 'common.noRecords' | t }}</td></tr>
                }
              </tbody>
            </table>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class NotificationSettingsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly injector = inject(Injector);

  protected readonly channelFields = CHANNEL_FIELDS;
  protected readonly busy = signal(false);
  protected testEmail = '';
  protected testPhone = '';

  protected readonly rules = apiResource({ stream: () => this.api.get<NotificationRule[]>('notifications/rules') });
  protected readonly channels = apiResource({ stream: () => this.api.get<ChannelSettings>('notifications/channels') });
  protected readonly outbox = apiResource({ stream: () => this.api.get<PagedResult<OutboxMessage>>('notifications/outbox', { pageSize: 100 }) });
  private readonly roles = apiResource({ stream: () => this.api.get<Role[]>('company/roles') });

  protected readonly channelForm = computed(() => {
    const c = this.channels.value();
    return c ? buildForm(CHANNEL_FIELDS, { ...c, smtpPassword: null, whatsAppToken: null } as unknown as Record<string, unknown>) : null;
  });

  protected readonly secretsInfo = computed(() => {
    const c = this.channels.value();
    if (!c) {
      return '';
    }

    return [c.hasSmtpPassword ? this.i18n.t('notifications.smtpStored') : null, c.hasWhatsAppToken ? this.i18n.t('notifications.tokenStored') : null].filter(Boolean).join(' · ');
  });

  protected channelsText(channels: string): string {
    return channels
      .split(',')
      .map((c) => c.trim())
      .filter((c) => c && c !== 'none')
      .map((c) => this.i18n.enum('notificationChannel', c))
      .join(', ') || '—';
  }

  protected recipients(rule: NotificationRule): string {
    const roles = this.roles.value() ?? [];
    const names = rule.roleIds.map((id) => roles.find((r) => r.id === id)?.name).filter(Boolean);
    return [...names, ...rule.emails, ...rule.phones, rule.notifyCustomerContacts ? this.i18n.t('notifications.customerContacts') : null, rule.notifyAssignees ? this.i18n.t('notifications.assignees') : null]
      .filter(Boolean)
      .join(', ');
  }

  protected async editRule(rule: NotificationRule): Promise<void> {
    const roles = this.roles.value() ?? [];
    const fields: FieldDef[] = [
      { key: 'isEnabled', label: 'notifications.enabled', type: 'checkbox', span: 'full' },
      {
        key: 'channels',
        label: 'notifications.channels',
        type: 'multiselect',
        options: ['inApp', 'email', 'whatsApp'].map((c) => ({ value: c, label: `enums.notificationChannel.${c}` })),
        span: 'full',
      },
      { key: 'roleIds', label: 'notifications.roles', type: 'multiselect', options: roles.map((r) => ({ value: r.id, label: r.name, literal: true })), span: 'full' },
      { key: 'emails', label: 'notifications.emails', type: 'chips' },
      { key: 'phones', label: 'notifications.phones', type: 'chips', hint: 'notifications.phonesHint' },
      { key: 'notifyCustomerContacts', label: 'notifications.customerContacts', type: 'checkbox' },
      { key: 'notifyAssignees', label: 'notifications.assignees', type: 'checkbox' },
      ...(EXPIRY_EVENTS.has(rule.event) ? [{ key: 'daysBefore', label: 'notifications.daysBefore', type: 'number' as const, min: 0, max: 180 }] : []),
    ];
    const saved = await actionContext(this.injector).form(
      {
        title: this.i18n.enum('notificationEvent', rule.event),
        fields,
        save: (api, v) =>
          api.put('notifications/rules', {
            event: rule.event,
            isEnabled: !!v['isEnabled'],
            channels: ((v['channels'] as string[]) ?? []).join(', ') || 'none',
            roleIds: v['roleIds'] ?? [],
            emails: v['emails'] ?? [],
            phones: v['phones'] ?? [],
            notifyCustomerContacts: !!v['notifyCustomerContacts'],
            notifyAssignees: !!v['notifyAssignees'],
            daysBefore: v['daysBefore'] ?? null,
          }),
        width: '720px',
      },
      { ...rule, channels: rule.channels.split(',').map((c) => c.trim()).filter((c) => c && c !== 'none') },
    );
    if (saved) {
      this.rules.reload();
    }
  }

  protected async saveChannels(form: FormGroup): Promise<void> {
    if (form.invalid) {
      form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    try {
      const value = readForm(CHANNEL_FIELDS, form);
      await firstValueFrom(this.api.put('notifications/channels', { smtpPort: 587, whatsAppTemplate: 'brocal_aviso', whatsAppLanguage: 'es_MX', ...value, useOwnSmtp: !!value['useOwnSmtp'], smtpUseSsl: !!value['smtpUseSsl'], whatsAppEnabled: !!value['whatsAppEnabled'] }));
      this.dialogs.success(this.i18n.t('common.saved'));
      this.channels.reload();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(): Promise<void> {
    await firstValueFrom(this.api.post('notifications/test', { email: this.testEmail || null, phone: this.testPhone || null }));
    this.dialogs.success(this.i18n.t('notifications.testQueued'));
    this.outbox.reload();
  }

  protected async retry(message: OutboxMessage): Promise<void> {
    await firstValueFrom(this.api.post(`notifications/outbox/${message.id}/retry`));
    this.outbox.reload();
  }
}

const ACCOUNTING_FIELDS: FieldDef[] = [
  { key: 'customersAccount', label: 'integrations.customersAccount', type: 'text', required: true },
  { key: 'revenueAccount', label: 'integrations.revenueAccount', type: 'text', required: true },
  { key: 'vatTransferredAccount', label: 'integrations.vatTransferred', type: 'text', required: true },
  { key: 'vatPendingAccount', label: 'integrations.vatPending', type: 'text', required: true },
  { key: 'bankAccount', label: 'integrations.bankAccount', type: 'text', required: true },
  { key: 'format', label: 'integrations.format', type: 'enum', enum: 'accountingExportFormat', values: ['excel', 'csv'], required: true },
];

/** Integraciones: API keys (GPS, external systems), Excel exports of everything and accounting vouchers for CONTPAQi/Aspel. */
@Component({
  selector: 'br-integrations',
  imports: [FormsModule, ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSlideToggleModule, MatTabsModule, TPipe, DateTimePipe, EntityForm, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.integrations' | t" icon="hub" [subtitle]="'integrations.hint' | t" />
      <div class="cl-toolbar-row br-filters">
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.from' | t }}</mat-label>
          <input matInput [matDatepicker]="fromPicker" [(ngModel)]="from" />
          <mat-datepicker-toggle matIconSuffix [for]="fromPicker" />
          <mat-datepicker #fromPicker />
        </mat-form-field>
        <mat-form-field class="br-filter" subscriptSizing="dynamic">
          <mat-label>{{ 'fields.to' | t }}</mat-label>
          <input matInput [matDatepicker]="toPicker" [(ngModel)]="to" />
          <mat-datepicker-toggle matIconSuffix [for]="toPicker" />
          <mat-datepicker #toPicker />
        </mat-form-field>
        <span class="cl-muted">{{ 'integrations.rangeHint' | t }}</span>
      </div>
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
        <mat-tab [label]="'integrations.tabs.exports' | t">
          <div class="br-grid br-tabs-body">
            @for (dataset of datasets.value() ?? []; track dataset.key) {
              <section class="br-card br-dataset">
                <mat-icon>table_view</mat-icon>
                <div>
                  <strong>{{ dataset.name }}</strong>
                  <small class="cl-muted">{{ 'modules.' + dataset.module | t }}</small>
                </div>
                <button mat-stroked-button type="button" (click)="export(dataset)"><mat-icon>download</mat-icon>Excel</button>
              </section>
            }
          </div>
        </mat-tab>
        <mat-tab [label]="'integrations.tabs.accounting' | t">
          @if (accountingForm(); as form) {
            <section class="br-card br-tabs-body">
              <p class="cl-muted">{{ 'integrations.accountingHint' | t }}</p>
              <br-entity-form [fields]="accountingFields" [form]="form" />
              <div class="br-actions" style="margin-top: 12px">
                <button mat-stroked-button type="button" (click)="saveAccounting(form)"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
                <span class="cl-spacer"></span>
                <button mat-flat-button type="button" (click)="exportAccounting()"><mat-icon>download</mat-icon>{{ 'integrations.exportVouchers' | t }}</button>
              </div>
            </section>
          }
        </mat-tab>
        <mat-tab [label]="'integrations.tabs.apiKeys' | t">
          <section class="br-card br-tabs-body">
            <div class="br-card__header">
              <h2>{{ 'integrations.apiKeys' | t }}</h2>
              <button mat-flat-button type="button" (click)="createKey()"><mat-icon>key</mat-icon>{{ 'integrations.newKey' | t }}</button>
            </div>
            <table class="br-table">
              <tbody>
                @for (key of keys.value() ?? []; track key.id) {
                  <tr [class.br-row-muted]="key.isRevoked">
                    <td><strong>{{ key.name }}</strong><div class="br-sub">{{ key.prefix }}… · {{ key.scopes.join(', ') }}</div></td>
                    <td class="br-wide">{{ 'integrations.lastUsed' | t }}: {{ key.lastUsedAt | dt }}</td>
                    <td>{{ key.isRevoked ? ('integrations.revoked' | t) : key.expiresOn ? ('integrations.expires' | t: { date: key.expiresOn }) : ('integrations.active' | t) }}</td>
                    <td class="cl-actions-cell">
                      @if (!key.isRevoked) {
                        <button mat-button type="button" (click)="revoke(key)">{{ 'integrations.revoke' | t }}</button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr><td class="br-table-empty">{{ 'integrations.noKeys' | t }}</td></tr>
                }
              </tbody>
            </table>
          </section>
          <section class="br-card">
            <h2>{{ 'integrations.gpsTitle' | t }}</h2>
            <p class="cl-muted">{{ 'integrations.gpsHint' | t }}</p>
            <pre class="br-codeblock">POST {{ apiUrl }}/integrations/gps/positions
X-Api-Key: brk_…
Content-Type: application/json

{{ gpsSample }}</pre>
            <p class="cl-muted">{{ 'integrations.feedsHint' | t }}</p>
            <pre class="br-codeblock">GET {{ apiUrl }}/integrations/feeds/tickets?from=2026-10-01&amp;pageSize=100      (tickets.read)
GET {{ apiUrl }}/integrations/feeds/exports/invoices?from=2026-10-01               (export.read)</pre>
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: `
    .br-dataset { display: flex; align-items: center; gap: 12px; }
    .br-dataset div { flex: 1; display: flex; flex-direction: column; }
    .br-codeblock { padding: 12px; border-radius: var(--cl-radius-sm, 8px); background: var(--mat-sys-surface-container-high); overflow-x: auto; font: 13px/1.5 ui-monospace, Consolas, monospace; }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class IntegrationsPage {
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly injector = inject(Injector);

  protected readonly accountingFields = ACCOUNTING_FIELDS;
  protected readonly apiUrl = API;
  protected readonly gpsSample = JSON.stringify(
    { source: 'mi-proveedor-gps', positions: [{ unitCode: 'AT-01', recordedAt: '2026-10-11T14:05:00-06:00', latitude: 18.0012, longitude: -93.3751, speedKmh: 54, odometer: 183500 }] },
    null,
    2,
  );
  protected from = firstOfMonth(-1);
  protected to = today();
  protected readonly busy = signal(false);

  protected readonly datasets = apiResource({ stream: () => this.api.get<ExportDataset[]>('integrations/exports') });
  protected readonly keys = apiResource({ stream: () => this.api.get<ApiKeyInfo[]>('integrations/api-keys') });
  private readonly accounting = apiResource({ stream: () => this.api.get<AccountingSettings>('integrations/accounting') });
  protected readonly accountingForm = computed(() => {
    const a = this.accounting.value();
    return a ? buildForm(ACCOUNTING_FIELDS, a as unknown as Record<string, unknown>) : null;
  });

  protected async export(dataset: ExportDataset): Promise<void> {
    await this.run(async () => saveFile(await firstValueFrom(this.api.download(`integrations/exports/${dataset.key}`, { from: this.from, to: this.to }))));
  }

  protected async saveAccounting(form: FormGroup): Promise<void> {
    if (form.valid) {
      await this.run(async () => {
        await firstValueFrom(this.api.put('integrations/accounting', readForm(ACCOUNTING_FIELDS, form)));
        this.dialogs.success(this.i18n.t('common.saved'));
      });
    }
  }

  protected async exportAccounting(): Promise<void> {
    await this.run(async () => saveFile(await firstValueFrom(this.api.download('integrations/accounting/export', { from: this.from, to: this.to }))));
  }

  protected async createKey(): Promise<void> {
    const ctx = actionContext(this.injector);
    const created = (await ctx.form(
      {
        title: this.i18n.t('integrations.newKey'),
        fields: [
          { key: 'name', label: 'fields.name', type: 'text', required: true, hint: 'integrations.keyNameHint' },
          {
            key: 'scopes',
            label: 'integrations.scopes',
            type: 'multiselect',
            required: true,
            options: [
              { value: 'gps.write', label: 'integrations.scopeGps' },
              { value: 'tickets.read', label: 'integrations.scopeTickets' },
              { value: 'export.read', label: 'integrations.scopeExport' },
            ],
            span: 'full',
          },
          { key: 'expiresOn', label: 'integrations.expiresOn', type: 'date' },
        ],
        save: (api, v) => api.post<CreatedApiKey>('integrations/api-keys', v),
        width: '560px',
      },
      { scopes: ['gps.write'] },
    )) as CreatedApiKey | null;
    if (created) {
      this.keys.reload();
      await this.dialogs.confirm(this.i18n.t('integrations.keyCreated'), this.i18n.t('integrations.keyCreatedMessage', { key: created.plainValue }), this.i18n.t('integrations.copied'));
      await navigator.clipboard?.writeText(created.plainValue).catch(() => undefined);
    }
  }

  protected async revoke(key: ApiKeyInfo): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('integrations.revoke'), this.i18n.t('integrations.revokeMessage', { name: key.name }), this.i18n.t('integrations.revoke'), true)) {
      await firstValueFrom(this.api.delete(`integrations/api-keys/${key.id}`));
      this.keys.reload();
    }
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
