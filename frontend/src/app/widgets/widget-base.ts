import { Injectable, inject } from '@angular/core';
import { I18n } from '../core/i18n';
import { BrocalSession } from '../core/session';

/**
 * Widgets run on the dashboards of ChronoLith, outside the Brocal layout: they load the texts and the
 * session (company and permissions of the user) themselves before calling the API.
 */
@Injectable({ providedIn: 'root' })
export class WidgetContext {
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  private ready: Promise<void> | null = null;

  ensure(): Promise<void> {
    this.ready ??= Promise.all([this.i18n.ready(), this.session.ensureLoaded()]).then(() => undefined);
    return this.ready;
  }
}

export const WIDGET_STYLES = `
  :host { display: block; height: 100%; overflow: auto; }
  .br-widget { display: flex; flex-direction: column; gap: 10px; padding: 12px 16px; }
  .br-widget__message { display: flex; align-items: center; gap: 8px; padding: 16px; margin: 0; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
  .br-widget__company { font: var(--mat-sys-label-small); color: var(--mat-sys-on-surface-variant); text-transform: uppercase; letter-spacing: .05em; }
  .br-widget__figures { display: flex; flex-wrap: wrap; gap: 12px 24px; }
  .br-widget__figure { display: flex; flex-direction: column; }
  .br-widget__figure strong { font: var(--mat-sys-headline-small); font-weight: 700; color: var(--mat-sys-primary); }
  .br-widget__figure span { font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); }
  .br-widget__list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
  .br-widget__list li { display: flex; align-items: center; gap: 8px; font: var(--mat-sys-body-small); }
  .br-widget__list .grow { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .br-widget__more { align-self: flex-end; font: var(--mat-sys-label-large); }
`;
