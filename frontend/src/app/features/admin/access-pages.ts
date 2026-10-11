import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';
import { AuditEntry, Member, ModuleInfo, Role } from '../../core/models';
import { P } from '../../core/permissions';
import { apiResource } from '../../core/resource';
import { BrocalSession } from '../../core/session';
import { FieldDef } from '../../shared/crud/fields';
import { ActionContext, ListConfig } from '../../shared/crud/list-page';
import { Dialogs } from '../../shared/dialogs';
import { PageHeader } from '../../shared/ui';

// ---- Members -------------------------------------------------------------------------------------------------

async function roleOptions(ctx: ActionContext) {
  const roles = await firstValueFrom(ctx.api.get<Role[]>('company/roles'));
  return roles.map((r) => ({ value: r.id, label: r.name, literal: true }));
}

function memberFields(options: { value: string; label: string; literal: boolean }[], invite: boolean): FieldDef[] {
  return [
    ...(invite ? [{ key: 'email', label: 'fields.email', type: 'text' as const, required: true, pattern: '^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$' }] : []),
    { key: 'fullName', label: 'fields.name', type: 'text', required: true },
    { key: 'roleIds', label: 'members.roles', type: 'multiselect', options, span: 'full' },
    { key: 'isTenantAdmin', label: 'members.isAdmin', type: 'checkbox', span: 'full' },
    { key: 'employeeId', label: 'members.employee', type: 'lookup', catalog: 'personnel/employees', hint: 'members.employeeHint' },
    { key: 'customerId', label: 'members.customer', type: 'lookup', catalog: 'catalogs/customers', hint: 'members.customerHint' },
    ...(invite
      ? [
          { key: 'createAccount', label: 'members.createAccount', type: 'checkbox' as const, span: 'full' as const, hint: 'members.createAccountHint' },
          { key: 'temporaryPassword', label: 'members.temporaryPassword', type: 'text' as const, showIf: (v: Record<string, unknown>) => v['createAccount'] === true },
        ]
      : []),
  ];
}

export const MEMBER_LIST: ListConfig<Member> = {
  key: 'users',
  icon: 'group',
  api: 'company/members',
  filters: [
    { key: 'search', label: 'common.search', type: 'search' },
    { key: 'status', label: 'fields.status', type: 'enum', enum: 'membershipStatus', values: ['invited', 'active', 'disabled'] },
    { key: 'portalUsers', label: 'members.portalOnly', type: 'toggle' },
  ],
  headerActions: [
    {
      key: 'invite',
      icon: 'person_add',
      label: 'members.invite',
      permissions: [P.accessUsers],
      run: async (ctx) => {
        const options = await roleOptions(ctx);
        const result = await ctx.form(
          { title: ctx.i18n.t('members.invite'), fields: memberFields(options, true), save: (api, v) => api.post('company/members', { ...v, roleIds: v['roleIds'] ?? [], createAccount: !!v['createAccount'] }) },
          { roleIds: [], isTenantAdmin: false, createAccount: false },
        );
        if (result) {
          ctx.dialogs.success(ctx.i18n.t('members.invited'));
        }

        return !!result;
      },
    },
  ],
  columns: [
    { key: 'fullName', label: 'fields.name', sub: (r) => r.email },
    { key: 'roleNames', label: 'members.roles', value: (r, t) => (r.isTenantAdmin ? `★ ${t('members.tenantAdmin')}` : r.roleNames.join(', ') || '—') },
    { key: 'employeeName', label: 'members.linkedTo', value: (r, t) => r.employeeName ?? (r.customerName ? t('members.portalOf', { customer: r.customerName }) : '—'), wide: true },
    { key: 'status', label: 'fields.status', type: 'status', enum: 'membershipStatus', sub: (r, t) => (r.isBound ? null : t('members.pendingFirstSignIn')) },
    { key: 'lastAccessAt', label: 'members.lastAccess', type: 'datetime', wide: true },
  ],
  rowActions: [
    {
      key: 'edit',
      icon: 'edit',
      label: 'common.edit',
      permissions: [P.accessUsers],
      run: async (ctx, row) => {
        const options = await roleOptions(ctx);
        return !!(await ctx.form({ title: row.fullName, fields: memberFields(options, false), save: (api, v) => api.put(`company/members/${row.id}`, { ...v, roleIds: v['roleIds'] ?? [] }) }, { ...row }));
      },
    },
    {
      key: 'disable',
      icon: 'person_off',
      label: 'members.disable',
      permissions: [P.accessUsers],
      visible: (r) => r.status !== 'disabled',
      run: async (ctx, row) => {
        await firstValueFrom(ctx.api.put(`company/members/${row.id}/status`, { enabled: false }));
        return true;
      },
    },
    {
      key: 'enable',
      icon: 'person',
      label: 'members.enable',
      permissions: [P.accessUsers],
      visible: (r) => r.status === 'disabled',
      run: async (ctx, row) => {
        await firstValueFrom(ctx.api.put(`company/members/${row.id}/status`, { enabled: true }));
        return true;
      },
    },
    {
      key: 'remove',
      icon: 'delete',
      label: 'members.remove',
      permissions: [P.accessUsers],
      run: async (ctx, row) => {
        if (!(await ctx.dialogs.confirm(ctx.i18n.t('members.remove'), ctx.i18n.t('members.removeMessage', { name: row.fullName }), ctx.i18n.t('common.remove'), true))) {
          return false;
        }

        await firstValueFrom(ctx.api.delete(`company/members/${row.id}`));
        return true;
      },
    },
  ],
  muted: (r) => r.status === 'disabled',
};

// ---- Audit log ------------------------------------------------------------------------------------------------

const AUDIT_ACTIONS = ['created', 'updated', 'deleted', 'statusChanged', 'signed', 'cancelled', 'reopened', 'stamped', 'exported', 'accessed'] as const;

export const AUDIT_LIST: ListConfig<AuditEntry> = {
  key: 'audit',
  icon: 'history',
  api: 'audit',
  pageSize: 50,
  filters: [
    { key: 'search', label: 'audit.search', type: 'search' },
    { key: 'action', label: 'audit.action', type: 'enum', enum: 'auditAction', values: AUDIT_ACTIONS },
    { key: 'from', label: 'fields.from', type: 'date' },
    { key: 'to', label: 'fields.to', type: 'date' },
  ],
  columns: [
    { key: 'occurredAt', label: 'audit.when', type: 'datetime', sub: (r) => r.ipAddress },
    { key: 'userName', label: 'audit.who' },
    { key: 'action', label: 'audit.action', type: 'enum', enum: 'auditAction' },
    { key: 'entityType', label: 'audit.record', sub: (r) => r.summary },
    { key: 'reason', label: 'fields.reason', wide: true },
  ],
  rowActions: [
    {
      key: 'changes',
      icon: 'difference',
      label: 'audit.viewChanges',
      visible: (r) => !!r.changes,
      run: async (ctx, row) => {
        let text = row.changes ?? '';
        try {
          text = JSON.stringify(JSON.parse(text), null, 2);
        } catch {
          // Plain text.
        }

        await ctx.dialogs.confirm(`${row.entityType} · ${row.summary ?? ''}`, text, ctx.i18n.t('common.close'));
        return false;
      },
    },
  ],
};

// ---- Roles -----------------------------------------------------------------------------------------------------

interface RoleDialogData {
  role: Role | null;
  modules: ModuleInfo[];
  enabled: string[];
  english: boolean;
}

@Component({
  selector: 'br-role-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatExpansionModule, MatFormFieldModule, MatInputModule, TPipe],
  template: `
    <h2 mat-dialog-title>{{ data.role ? data.role.name : ('roles.new' | t) }}</h2>
    <mat-dialog-content>
      <div class="cl-form-grid">
        <mat-form-field>
          <mat-label>{{ 'fields.name' | t }}</mat-label>
          <input matInput [formControl]="name" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.description' | t }}</mat-label>
          <input matInput [formControl]="description" />
        </mat-form-field>
      </div>
      <p class="cl-muted">{{ 'roles.permissionsHint' | t: { n: selected().size } }}</p>
      <mat-accordion multi>
        @for (module of modules; track module.code) {
          <mat-expansion-panel [disabled]="!data.enabled.includes(module.code)">
            <mat-expansion-panel-header>
              <mat-panel-title>
                <mat-checkbox [checked]="all(module)" [indeterminate]="some(module)" (click)="$event.stopPropagation()" (change)="toggleModule(module, $event.checked)" [disabled]="!data.enabled.includes(module.code)" />
                {{ module.number }}. {{ data.english ? module.nameEn : module.name }}
              </mat-panel-title>
              <mat-panel-description>{{ count(module) }}/{{ module.permissions.length }}</mat-panel-description>
            </mat-expansion-panel-header>
            <div class="br-permissions">
              @for (permission of module.permissions; track permission.code) {
                <mat-checkbox [checked]="selected().has(permission.code)" (change)="toggle(permission.code, $event.checked)">
                  {{ data.english ? permission.nameEn : permission.name }}
                  <small class="cl-muted">{{ permission.code }}</small>
                </mat-checkbox>
              }
            </div>
          </mat-expansion-panel>
        }
      </mat-accordion>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | t }}</button>
      <button mat-flat-button type="button" [disabled]="name.invalid || saving()" (click)="save()">{{ 'common.save' | t }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .br-permissions { display: flex; flex-direction: column; gap: 4px; }
    .br-permissions small { margin-left: 6px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RoleDialog {
  protected readonly data = inject<RoleDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<RoleDialog>);
  private readonly api = inject(BrocalApi);
  protected readonly modules = this.data.modules.filter((m) => m.permissions.length > 0);
  protected readonly name = new FormControl(this.data.role?.name ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] });
  protected readonly description = new FormControl(this.data.role?.description ?? '');
  protected readonly selected = signal(new Set(this.data.role?.permissions ?? []));
  protected readonly saving = signal(false);

  protected count(module: ModuleInfo): number {
    return module.permissions.filter((p) => this.selected().has(p.code)).length;
  }

  protected all(module: ModuleInfo): boolean {
    return this.count(module) === module.permissions.length;
  }

  protected some(module: ModuleInfo): boolean {
    const n = this.count(module);
    return n > 0 && n < module.permissions.length;
  }

  protected toggle(code: string, checked: boolean): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (checked) {
        next.add(code);
      } else {
        next.delete(code);
      }

      return next;
    });
  }

  protected toggleModule(module: ModuleInfo, checked: boolean): void {
    for (const permission of module.permissions) {
      this.toggle(permission.code, checked);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    const body = { name: this.name.value.trim(), description: this.description.value?.trim() || null, permissions: [...this.selected()] };
    try {
      await firstValueFrom(this.data.role ? this.api.put(`company/roles/${this.data.role.id}`, body) : this.api.post('company/roles', body));
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}

/** Roles of the company: which permissions of each contracted module a role grants (field operator, office, management…). */
@Component({
  selector: 'br-roles',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.roles' | t" icon="admin_panel_settings" [subtitle]="'roles.hint' | t">
        <button mat-flat-button type="button" (click)="open(null)"><mat-icon>add</mat-icon>{{ 'roles.new' | t }}</button>
      </br-page-header>
      @if (roles.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <div class="br-grid">
        @for (role of roles.value() ?? []; track role.id) {
          <section class="br-card br-role">
            <div class="br-card__header">
              <h2>{{ role.name }}</h2>
              <button mat-icon-button type="button" (click)="open(role)" [attr.aria-label]="'common.edit' | t"><mat-icon>edit</mat-icon></button>
              <button mat-icon-button type="button" (click)="remove(role)" [disabled]="role.members > 0" [attr.aria-label]="'common.delete' | t"><mat-icon>delete</mat-icon></button>
            </div>
            <p class="cl-muted">{{ role.description }}</p>
            <div class="br-actions">
              <span class="cl-chip">{{ 'roles.permissionsCount' | t: { n: role.permissions.length } }}</span>
              <span class="cl-chip cl-chip--info">{{ 'roles.membersCount' | t: { n: role.members } }}</span>
              @if (role.templateKey) {
                <span class="cl-chip">{{ 'roles.template' | t }}</span>
              }
            </div>
          </section>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RolesPage {
  private readonly api = inject(BrocalApi);
  private readonly dialog = inject(MatDialog);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);

  protected readonly roles = apiResource({ stream: () => this.api.get<Role[]>('company/roles') });
  private readonly modules = apiResource({ stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }) });
  private readonly english = computed(() => this.i18n.language() === 'en');

  protected async open(role: Role | null): Promise<void> {
    const modules = this.modules.value() ?? (await firstValueFrom(this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false })));
    const saved = await firstValueFrom(
      this.dialog.open(RoleDialog, { data: { role, modules, enabled: this.session.tenant()?.enabledModules ?? [], english: this.english() } satisfies RoleDialogData, width: '820px', maxWidth: '96vw' }).afterClosed(),
    );
    if (saved) {
      this.roles.reload();
      await this.session.refresh();
      this.dialogs.success(this.i18n.t('common.saved'));
    }
  }

  protected async remove(role: Role): Promise<void> {
    if (await this.dialogs.confirm(this.i18n.t('common.deleteTitle'), this.i18n.t('common.deleteMessage', { name: role.name }), this.i18n.t('common.delete'), true)) {
      await firstValueFrom(this.api.delete(`company/roles/${role.id}`));
      this.roles.reload();
    }
  }
}
