import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from './api';
import { BrocalSession } from './session';

export type LicenseState = 'active' | 'expiring' | 'grace' | 'expired';

/** `GET /api/license/status`: notice shown to every user. */
export interface LicenseStatus {
  state: LicenseState;
  expiresAt: string;
  graceEndsAt: string;
  daysRemaining: number;
  allowsWrites: boolean;
  development: boolean;
  customer: string;
}

/** `GET /api/license`: "Licencia" page of the platform administrators. */
export interface LicenseDetails extends LicenseStatus {
  id: string;
  product: string;
  instanceId: string;
  licensedInstanceId: string | null;
  issuedAt: string;
  graceDays: number;
  source: 'stored' | 'configuration' | 'file';
  products: { code: string; limits: Record<string, number> }[];
  usage: { key: string; used: number; limit: number | null }[];
  notes: string | null;
}

/**
 * ChronoLith license of the installation. Only a standalone API validates it (as a module of ChronoLith the Shell
 * does and these endpoints do not exist), so nothing is requested unless the API reports the standalone mode.
 */
@Injectable({ providedIn: 'root' })
export class LicenseService {
  private readonly api = inject(BrocalApi);
  private readonly session = inject(BrocalSession);

  readonly status = signal<LicenseStatus | null>(null);

  /** The API validates the license (standalone mode). */
  readonly enabled = computed(() => this.session.mode() === 'Standalone');

  /** Something to tell the users: about to expire, expired (grace or read-only) or a development license. */
  readonly notice = computed(() => {
    const status = this.status();
    return status && (status.state !== 'active' || status.development) ? status : null;
  });

  async refresh(): Promise<void> {
    if (!this.enabled()) {
      this.status.set(null);
      return;
    }

    try {
      this.status.set(await firstValueFrom(this.api.get<LicenseStatus>('license/status', undefined, { tenant: false })));
    } catch {
      // The notice is optional: the HTTP error handler already reported the failure.
    }
  }

  details(): Promise<LicenseDetails> {
    return firstValueFrom(this.api.get<LicenseDetails>('license', undefined, { tenant: false }));
  }

  async upload(token: string): Promise<LicenseDetails> {
    const details = await firstValueFrom(this.api.put<LicenseDetails>('license', { token }, { tenant: false }));
    await this.refresh();
    return details;
  }
}
