import { Clipboard } from '@angular/cdk/clipboard';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { BrocalApi } from '../../core/api';
import { DatePipe, DateTimePipe } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { LicenseDetails, LicenseService } from '../../core/license';
import { ModuleInfo } from '../../core/models';
import { apiResource } from '../../core/resource';
import { Dialogs, describeError } from '../../shared/dialogs';
import { PageHeader, StatusChip } from '../../shared/ui';

/**
 * Licencia (platform administrators, standalone mode): the ChronoLith license of the installation, shared by every
 * company. Customer, validity, products and limits with their usage, the installation id to request a license and
 * the field to paste a new one (it is activated without restarting).
 */
@Component({
  selector: 'br-license-page',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatTooltipModule, TPipe, DatePipe, DateTimePipe, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.license' | t" icon="verified_user" [subtitle]="'license.hint' | t" />
      @if (loading() || busy()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (details(); as l) {
        @if (l.development) {
          <div class="br-warning-box br-warning-box--info"><mat-icon>info</mat-icon>{{ 'license.notice.development' | t }}</div>
        }

        <section class="br-card">
          <h2>{{ 'license.current' | t }}</h2>
          <dl class="br-dl">
            <div><dt>{{ 'license.customer' | t }}</dt><dd>{{ l.customer }}</dd></div>
            <div><dt>{{ 'license.state' | t }}</dt><dd><br-status enum="licenseState" [value]="l.state" /></dd></div>
            <div><dt>{{ 'license.expiresAt' | t }}</dt><dd>{{ l.expiresAt | d }}</dd></div>
            <div><dt>{{ 'license.graceEndsAt' | t }}</dt><dd>{{ l.graceEndsAt | d }} · {{ 'license.graceDays' | t: { n: l.graceDays } }}</dd></div>
            <div><dt>{{ 'license.daysRemaining' | t }}</dt><dd>{{ l.daysRemaining }}</dd></div>
            <div><dt>{{ 'license.issuedAt' | t }}</dt><dd>{{ l.issuedAt | dt }}</dd></div>
            <div><dt>{{ 'license.source' | t }}</dt><dd>{{ 'license.sources.' + l.source | t }}</dd></div>
            <div><dt>{{ 'license.boundTo' | t }}</dt><dd>{{ l.licensedInstanceId ? ('license.boundYes' | t) : ('license.boundAny' | t) }}</dd></div>
          </dl>
          @if (l.notes) {
            <p class="cl-muted">{{ l.notes }}</p>
          }
        </section>

        <section class="br-card">
          <h2>{{ 'license.products' | t }}</h2>
          <div class="br-chips">
            @for (p of l.products; track p.code) {
              <span class="cl-chip">{{ p.code === '*' ? ('license.allProducts' | t) : p.code }}</span>
            }
          </div>
          <div class="cl-table-wrapper">
            <table class="br-table">
              <thead>
                <tr>
                  <th>{{ 'license.limit' | t }}</th>
                  <th class="br-end">{{ 'license.used' | t }}</th>
                  <th class="br-end">{{ 'license.allowed' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (u of l.usage; track u.key) {
                  <tr>
                    <td>{{ limitLabel(u.key) }}</td>
                    <td class="br-end" [class.br-error-text]="u.limit !== null && u.used >= u.limit">{{ u.used }}</td>
                    <td class="br-end">{{ u.limit ?? ('license.unlimited' | t) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="cl-muted">{{ 'license.limitsHint' | t }}</p>
        </section>

        <section class="br-card">
          <h2>{{ 'license.modules' | t }}</h2>
          @if (grantedModules(); as granted) {
            @if (granted.length > 0) {
              <div class="br-chips">
                @for (m of granted; track m.code) {
                  <span class="cl-chip"><mat-icon class="br-inline-icon">{{ m.icon }}</mat-icon>{{ m.number }}. {{ english() ? m.nameEn : m.name }}</span>
                }
              </div>
            } @else {
              <p>{{ 'license.modulesAll' | t }}</p>
            }
          }
          <p class="cl-muted">{{ 'license.modulesHint' | t }}</p>
        </section>

        <section class="br-card">
          <h2>{{ 'license.instance' | t }}</h2>
          <p class="cl-muted">{{ 'license.instanceHint' | t }}</p>
          <div class="br-license-instance">
            <code>{{ l.instanceId }}</code>
            <button mat-stroked-button type="button" (click)="copy(l.instanceId)"><mat-icon>content_copy</mat-icon>{{ 'license.copy' | t }}</button>
          </div>
        </section>
      }

      <section class="br-card">
        <h2>{{ 'license.load' | t }}</h2>
        <p class="cl-muted">{{ 'license.loadHint' | t }}</p>
        <mat-form-field class="br-full">
          <mat-label>{{ 'license.token' | t }}</mat-label>
          <textarea matInput [formControl]="token" rows="5" spellcheck="false" autocomplete="off"></textarea>
        </mat-form-field>
        @if (error(); as error) {
          <div class="br-warning-box br-warning-box--error"><mat-icon>error</mat-icon>{{ error }}</div>
        }
        <div class="br-actions">
          <button mat-flat-button type="button" (click)="upload()" [disabled]="token.invalid || busy()"><mat-icon>upload</mat-icon>{{ 'license.apply' | t }}</button>
        </div>
      </section>
    </div>
  `,
  styles: `
    .br-license-instance {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;

      code {
        font: var(--mat-sys-title-medium);
        font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace;
        overflow-wrap: anywhere;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LicensePage {
  private readonly license = inject(LicenseService);
  private readonly api = inject(BrocalApi);
  private readonly dialogs = inject(Dialogs);
  private readonly clipboard = inject(Clipboard);
  private readonly i18n = inject(I18n);

  protected readonly details = signal<LicenseDetails | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly token = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly english = computed(() => this.i18n.language() === 'en');

  private readonly modules = apiResource({
    params: () => this.details()?.id,
    stream: () => this.api.get<ModuleInfo[]>('platform/modules', undefined, { tenant: false }),
  });

  /**
   * Optional modules the license grants (it lists them as products brocal.<module>); empty when it grants every
   * module, null while loading.
   */
  protected readonly grantedModules = computed(() => {
    const modules = this.modules.value();
    if (!modules) {
      return null;
    }

    return modules.some((m) => !m.licensed) ? modules.filter((m) => m.licensed && !m.isCore) : [];
  });

  /** Labels of the limits Brocal understands; any other key the license may carry is shown as is. */
  private readonly limitLabels: Record<string, string> = {
    users: 'license.limits.users',
    portalusers: 'license.limits.portalusers',
    companies: 'license.limits.companies',
    units: 'license.limits.units',
    employees: 'license.limits.employees',
    tickets: 'license.limits.tickets',
    storagemb: 'license.limits.storagemb',
    apikeys: 'license.limits.apikeys',
  };

  constructor() {
    void this.load();
  }

  protected limitLabel(key: string): string {
    const label = this.limitLabels[key];
    return label ? this.i18n.t(label) : key;
  }

  protected copy(value: string): void {
    if (this.clipboard.copy(value)) {
      this.dialogs.success(this.i18n.t('license.copied'));
    }
  }

  protected async upload(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.details.set(await this.license.upload(this.token.value.trim()));
      this.token.reset();
      this.dialogs.success(this.i18n.t('license.applied'));
    } catch (error) {
      // The HTTP error handler shows it too; here it stays next to the field.
      this.error.set(describeError(error, this.i18n));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.details.set(await this.license.details());
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.loading.set(false);
    }
  }
}
