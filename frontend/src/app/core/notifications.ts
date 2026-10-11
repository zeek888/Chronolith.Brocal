import { Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from './api';
import { MyNotifications, UserNotification } from './models';

/** Session actions of the standalone application (Keycloak); absent inside ChronoLith (the Shell owns the session). */
export interface BrocalAuth {
  username(): string;
  logout(): void;
  accountConsole(): void;
}

export const BROCAL_AUTH = new InjectionToken<BrocalAuth>('BROCAL_AUTH');

/** In-app inbox (signed tickets, expiries, overdue invoices…), refreshed while Brocal is open. */
@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly api = inject(BrocalApi);
  private timer: ReturnType<typeof setInterval> | null = null;

  readonly items = signal<UserNotification[]>([]);
  readonly unread = signal(0);
  readonly hasUnread = computed(() => this.unread() > 0);

  start(): void {
    void this.refresh();
    this.timer ??= setInterval(() => void this.refresh(), 90_000);
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  async refresh(): Promise<void> {
    if (!navigator.onLine) {
      return;
    }

    try {
      const inbox = await firstValueFrom(this.api.get<MyNotifications>('me/notifications', { take: 20 }));
      this.items.set(inbox.items);
      this.unread.set(inbox.unread);
    } catch {
      // Ignored: the inbox is refreshed again in a while.
    }
  }

  async markRead(ids?: string[]): Promise<void> {
    await firstValueFrom(this.api.post('me/notifications/read', { ids: ids ?? null }));
    await this.refresh();
  }
}
