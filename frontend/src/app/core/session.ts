import { HttpErrorResponse } from '@angular/common/http';
import { Directive, Injectable, TemplateRef, ViewContainerRef, computed, effect, inject, input, signal } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BrocalApi, TenantHeader } from './api';
import { CurrentTenant, CurrentUser, MyContext, MyTenant } from './models';
import { BrocalNav } from './nav';

const TENANT_KEY = 'brocal.tenant';

/**
 * Who the user is in Brocal and on which company they work: memberships, the selected company (branding,
 * contracted modules, permissions of the user's roles) and the platform role. The selected company travels
 * to the API in `X-Tenant` and is remembered per device.
 */
@Injectable({ providedIn: 'root' })
export class BrocalSession {
  private readonly api = inject(BrocalApi);
  private readonly header = inject(TenantHeader);
  private loading: Promise<void> | null = null;

  readonly user = signal<CurrentUser | null>(null);
  readonly tenants = signal<MyTenant[]>([]);
  readonly tenant = signal<CurrentTenant | null>(null);
  readonly mode = signal<string>('ChronoLith');
  readonly version = signal<string>('');
  readonly loaded = signal(false);
  readonly error = signal<string | null>(null);

  readonly permissions = computed(() => new Set(this.tenant()?.permissions ?? []));
  readonly modules = computed(() => new Set(this.tenant()?.enabledModules ?? []));
  readonly isPlatformAdmin = computed(() => this.user()?.isPlatformAdmin ?? false);
  readonly isPortalUser = computed(() => !!this.tenant()?.membership.customerId);
  readonly isTenantAdmin = computed(() => this.tenant()?.membership.isTenantAdmin ?? false);
  readonly appName = computed(() => this.tenant()?.branding.appName ?? 'Brocal');

  /** Loads the session once (route guards and widgets call it). */
  ensureLoaded(): Promise<void> {
    this.loading ??= this.load().catch((error: unknown) => {
      this.loading = null;
      this.error.set(describe(error));
    });
    return this.loading;
  }

  can(...anyOf: string[]): boolean {
    const permissions = this.permissions();
    return anyOf.length === 0 || anyOf.some((p) => permissions.has(p));
  }

  hasModule(code: string): boolean {
    return this.modules().has(code);
  }

  async switchTenant(slug: string): Promise<void> {
    await this.selectTenant(slug);
  }

  /** Reloads the selected company (after changing its settings, branding or roles). */
  async refresh(): Promise<void> {
    const slug = this.header.slug();
    if (slug) {
      await this.selectTenant(slug);
    }
  }

  private async load(): Promise<void> {
    // Binds pending invitations (by verified e-mail) to this account before reading the memberships.
    await firstValueFrom(this.api.post('me/session', null, { tenant: false }));
    const context = await firstValueFrom(this.api.get<MyContext>('me/context', undefined, { tenant: false }));
    this.user.set(context.user);
    this.tenants.set(context.tenants);
    this.mode.set(context.mode);
    this.version.set(context.version);

    const stored = readStored();
    const preferred = context.tenants.find((t) => t.slug === stored && t.status !== 'cancelled') ?? context.tenants.find((t) => t.status !== 'cancelled');
    if (preferred) {
      await this.selectTenant(preferred.slug);
    }

    this.error.set(null);
    this.loaded.set(true);
  }

  private async selectTenant(slug: string): Promise<void> {
    const previous = this.header.slug();
    this.header.slug.set(slug);
    try {
      const context = await firstValueFrom(this.api.get<MyContext>('me/context'));
      this.tenant.set(context.tenant);
      this.user.set(context.user);
      this.tenants.set(context.tenants);
      store(slug);
    } catch (error) {
      this.header.slug.set(previous);
      throw error;
    }
  }
}

function readStored(): string | null {
  try {
    return localStorage.getItem(TENANT_KEY);
  } catch {
    return null;
  }
}

function store(slug: string): void {
  try {
    localStorage.setItem(TENANT_KEY, slug);
  } catch {
    // Storage unavailable (private mode): the first company is used next time.
  }
}

function describe(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const detail = (error.error as { detail?: string } | null)?.detail;
    return detail ?? `${error.status} ${error.statusText}`;
  }

  return error instanceof Error ? error.message : String(error);
}

/** Route guard: loads the session before showing Brocal pages. */
export const sessionReady: CanActivateFn = async () => {
  await inject(BrocalSession).ensureLoaded();
  return true;
};

/** Route guard factory: any of the permissions (and their modules enabled), otherwise back to the home page. */
export function requires(...anyOf: string[]): CanActivateFn {
  return (route) => (inject(BrocalSession).can(...anyOf) ? true : inject(BrocalNav).home(route));
}

/** Platform administrators only (companies, quotas, modules). */
export const platformAdminOnly: CanActivateFn = (route) => (inject(BrocalSession).isPlatformAdmin() ? true : inject(BrocalNav).home(route));

/** Shows the content when the user has any of the permissions: `*brCan="[p.ticketsCapture]"`. */
@Directive({ selector: '[brCan]' })
export class CanDirective {
  readonly brCan = input.required<readonly string[] | string>();

  constructor() {
    const session = inject(BrocalSession);
    const template = inject(TemplateRef);
    const container = inject(ViewContainerRef);
    let visible = false;

    effect(() => {
      const required = this.brCan();
      const allowed = session.can(...(typeof required === 'string' ? [required] : required));
      if (allowed && !visible) {
        container.createEmbeddedView(template);
      }

      if (!allowed && visible) {
        container.clear();
      }

      visible = allowed;
    });
  }
}
