import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localeEsMx from '@angular/common/locales/es-MX';
import { ApplicationConfig, Component, Injectable, LOCALE_ID, inject, isDevMode, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { MatIconRegistry } from '@angular/material/icon';
import { MAT_TOOLTIP_DEFAULT_OPTIONS } from '@angular/material/tooltip';
import { Title } from '@angular/platform-browser';
import { RouterOutlet, RouterStateSnapshot, TitleStrategy, provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { Translation, TranslocoLoader, TranslocoService, getBrowserLang, provideTransloco } from '@jsverse/transloco';
import { Observable, filter, firstValueFrom, merge, of } from 'rxjs';
import { routes } from '../brocal.routes';
import { BROCAL_MODE, RUNTIME_CONFIG, RuntimeConfig } from '../core/config';
import { BROCAL_AUTH } from '../core/notifications';
import { BrocalSession } from '../core/session';
import { BrocalTheme } from '../core/theme/brocal-theme';
import { StandaloneAuth, standaloneAuthInterceptor, standaloneErrorInterceptor } from './standalone-auth';

registerLocaleData(localeEsMx);

const LANGUAGE_KEY = 'brocal.language';

/** All the texts live in the `brocal` scope (inline loader); the root translations are empty. */
@Injectable({ providedIn: 'root' })
class EmptyLoader implements TranslocoLoader {
  getTranslation(): Observable<Translation> {
    return of({});
  }
}

/** "<page> · <company app name>"; route titles are translation keys of the scope. */
@Injectable()
class StandaloneTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(BrocalSession);
  private last: RouterStateSnapshot | null = null;

  constructor() {
    super();
    merge(this.transloco.langChanges$, this.transloco.events$.pipe(filter((e) => e.type === 'translationLoadSuccess'))).subscribe(() => {
      if (this.last) {
        this.updateTitle(this.last);
      }
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.last = snapshot;
    const key = this.buildTitle(snapshot);
    const page = key ? this.transloco.translate(key) : null;
    const app = this.session.appName();
    this.title.setTitle(page && page !== key ? `${page} · ${app}` : app);
  }
}

@Component({ selector: 'br-app', imports: [RouterOutlet], template: '<router-outlet />' })
export class StandaloneApp {}

/** Keycloak first (redirects to the login when there is no session), then the language of the device. */
async function initialize(): Promise<void> {
  const auth = inject(StandaloneAuth);
  const transloco = inject(TranslocoService);
  inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
  inject(BrocalTheme);

  let stored: string | null = null;
  try {
    stored = localStorage.getItem(LANGUAGE_KEY);
  } catch {
    stored = null;
  }

  const language = [stored, getBrowserLang()].find((l) => l === 'es' || l === 'en') ?? 'es';
  transloco.setActiveLang(language);
  transloco.langChanges$.subscribe((lang) => {
    document.documentElement.lang = lang;
    try {
      localStorage.setItem(LANGUAGE_KEY, lang);
    } catch {
      // Ignore storage errors.
    }
  });
  await firstValueFrom(transloco.load(language));
  await auth.init(language);
}

export function createStandaloneConfig(runtime: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      { provide: RUNTIME_CONFIG, useValue: runtime },
      { provide: BROCAL_MODE, useValue: 'standalone' },
      { provide: BROCAL_AUTH, useExisting: StandaloneAuth },
      { provide: LOCALE_ID, useValue: 'es-MX' },
      provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ scrollPositionRestoration: 'top' })),
      { provide: TitleStrategy, useClass: StandaloneTitleStrategy },
      provideHttpClient(withFetch(), withInterceptors([standaloneAuthInterceptor, standaloneErrorInterceptor])),
      provideTransloco({
        config: {
          availableLangs: ['es', 'en'],
          defaultLang: 'es',
          fallbackLang: 'es',
          missingHandler: { useFallbackTranslation: true, logMissingKey: isDevMode() },
          reRenderOnLangChange: true,
          prodMode: !isDevMode(),
        },
        loader: EmptyLoader,
      }),
      { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline', subscriptSizing: 'dynamic' } },
      { provide: MAT_TOOLTIP_DEFAULT_OPTIONS, useValue: { showDelay: 400, hideDelay: 0, touchendHideDelay: 0 } },
      provideAppInitializer(initialize),
    ],
  };
}
