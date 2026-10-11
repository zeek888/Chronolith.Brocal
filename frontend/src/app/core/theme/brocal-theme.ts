import { OverlayContainer } from '@angular/cdk/overlay';
import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrocalApi } from '../api';
import { BROCAL_MODE } from '../config';
import { Branding, ChronoLithTheme, ColorSchemePreference, ThemePalette } from '../models';
import { BrocalSession } from '../session';
import { buildThemeCss } from './theme-generator';

/** Class of the elements that take the company theme inside ChronoLith (Brocal area and its overlays). */
export const THEMED_CLASS = 'brocal-themed';

const SCHEME_KEY = 'brocal.colorScheme';

/** ChronoLith default palette (same seeds as the Shell). */
export const CHRONOLITH_PALETTE: ThemePalette = {
  primary: '#0B365B',
  secondary: '#2874A6',
  tertiary: '#E58E38',
  neutral: '#5E6872',
  error: '#D64532',
  success: '#5A9E34',
  warning: '#E0A82E',
  info: '#2C9EC8',
};

/**
 * Applies the look of the selected company. Each company either follows the style configured in
 * ChronoLith or has its own palette, corners and font:
 * - Inside ChronoLith the Shell already themes the page; a company with its own palette re-themes only the
 *   Brocal area (and dialogs/menus while Brocal is open).
 * - Standalone, the theme (ChronoLith's or the company's), color scheme and density go on `<html>`.
 */
@Injectable({ providedIn: 'root' })
export class BrocalTheme {
  private readonly document = inject(DOCUMENT);
  private readonly session = inject(BrocalSession);
  private readonly api = inject(BrocalApi);
  private readonly overlay = inject(OverlayContainer);
  private readonly standalone = inject(BROCAL_MODE) === 'standalone';
  private readonly darkQuery = this.document.defaultView?.matchMedia('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.darkQuery?.matches ?? false);
  private readonly chronoLith = signal<ChronoLithTheme | null>(null);
  private styleElement: HTMLStyleElement | null = null;
  private overlayAttached = false;

  /** Live preview of the branding editor (null = the saved branding). */
  readonly preview = signal<Branding | null>(null);

  readonly branding = computed(() => this.preview() ?? this.session.tenant()?.branding ?? null);

  /** User choice on this device (standalone); null follows the company default. */
  readonly userScheme = signal<ColorSchemePreference | null>(readScheme());

  readonly isDark = computed(() => {
    const scheme = this.userScheme() ?? this.branding()?.defaultColorScheme ?? 'system';
    return scheme === 'dark' || (scheme === 'system' && this.systemDark());
  });

  readonly logoUrl = computed(() => {
    const branding = this.branding();
    if (!branding) {
      return null;
    }

    return (this.isDark() && branding.logoDarkUrl) || branding.logoUrl || (branding.useChronoLithTheme ? this.chronoLith()?.logoUrl ?? null : null);
  });

  constructor() {
    this.darkQuery?.addEventListener('change', (event) => this.systemDark.set(event.matches));
    effect(() => this.apply(this.branding(), this.chronoLith()));

    if (this.standalone) {
      effect(() => {
        const root = this.document.documentElement;
        const dark = this.isDark();
        root.style.colorScheme = dark ? 'dark' : 'light';
        root.classList.toggle('cl-dark', dark);
      });

      effect(() => {
        const density = this.branding()?.density ?? 0;
        const root = this.document.documentElement;
        [-1, -2, -3].forEach((level) => root.classList.toggle(`cl-density-${level}`, level === density));
      });
    }
  }

  toggleDark(): void {
    const next: ColorSchemePreference = this.isDark() ? 'light' : 'dark';
    this.userScheme.set(next);
    try {
      localStorage.setItem(SCHEME_KEY, next);
    } catch {
      // Ignore storage errors.
    }
  }

  /** Inside ChronoLith: dialogs and menus of Brocal take the company theme while the module is open. */
  attachOverlay(): void {
    this.overlayAttached = true;
    this.syncOverlay();
  }

  detachOverlay(): void {
    this.overlayAttached = false;
    this.overlay.getContainerElement().classList.remove(THEMED_CLASS);
  }

  private apply(branding: Branding | null, chronoLith: ChronoLithTheme | null): void {
    if (this.standalone) {
      if (branding?.useChronoLithTheme && !chronoLith) {
        void this.loadChronoLithTheme();
      }

      const palette = !branding ? CHRONOLITH_PALETTE : branding.useChronoLithTheme ? (chronoLith?.palette ?? CHRONOLITH_PALETTE) : branding.palette;
      this.write(buildThemeCss({ palette, corners: branding?.corners, fontFamily: branding?.useChronoLithTheme ? null : branding?.fontFamily }, 'html'));
      return;
    }

    // Inside ChronoLith the Shell theme applies unless the company has its own look.
    this.write(branding && !branding.useChronoLithTheme ? buildThemeCss({ palette: branding.palette, corners: branding.corners, fontFamily: branding.fontFamily }, `.${THEMED_CLASS}`) : '');
    this.syncOverlay();
  }

  private syncOverlay(): void {
    const own = !this.standalone && !!this.branding() && !this.branding()!.useChronoLithTheme;
    this.overlay.getContainerElement().classList.toggle(THEMED_CLASS, this.overlayAttached && own);
  }

  private write(css: string): void {
    if (!this.styleElement) {
      this.styleElement = this.document.createElement('style');
      this.styleElement.id = 'brocal-company-theme';
      this.document.head.appendChild(this.styleElement);
    }

    this.styleElement.textContent = css;
  }

  private loading = false;

  private async loadChronoLithTheme(): Promise<void> {
    if (this.loading) {
      return;
    }

    this.loading = true;
    try {
      this.chronoLith.set(await firstValueFrom(this.api.get<ChronoLithTheme>('platform/chronolith-theme', undefined, { tenant: false })));
    } catch {
      this.chronoLith.set({ name: 'ChronoLith', palette: CHRONOLITH_PALETTE, appName: 'ChronoLith', logoUrl: null, density: 0, defaultColorScheme: 'light' });
    }
  }
}

function readScheme(): ColorSchemePreference | null {
  try {
    const value = localStorage.getItem(SCHEME_KEY);
    return value === 'light' || value === 'dark' || value === 'system' ? value : null;
  } catch {
    return null;
  }
}
