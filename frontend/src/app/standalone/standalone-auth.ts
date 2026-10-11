import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import Keycloak from 'keycloak-js';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { API, RUNTIME_CONFIG } from '../core/config';
import { I18n } from '../core/i18n';
import { BrocalAuth } from '../core/notifications';
import { describeError } from '../shared/dialogs';

/**
 * Keycloak session of the standalone application (Authorization Code + PKCE). Inside ChronoLith this class
 * is not used: the Shell owns the session and adds the token to the calls to the registered API.
 */
@Injectable({ providedIn: 'root' })
export class StandaloneAuth implements BrocalAuth {
  private readonly config = inject(RUNTIME_CONFIG);
  private readonly i18n = inject(I18n);
  private readonly keycloak = new Keycloak({ url: this.config.keycloak.url, realm: this.config.keycloak.realm, clientId: this.config.keycloak.clientId });
  private readonly user = signal('');
  private locale: string | undefined;

  async init(locale?: string): Promise<void> {
    this.locale = locale;
    this.keycloak.onTokenExpired = () => {
      this.keycloak.updateToken(30).catch(() => this.login());
    };
    this.keycloak.onAuthLogout = () => void this.login();

    const authenticated = await this.keycloak.init({ pkceMethod: 'S256', checkLoginIframe: false });
    if (!authenticated) {
      await this.login();
      return new Promise<void>(() => undefined);
    }

    this.user.set((this.keycloak.tokenParsed?.['preferred_username'] as string | undefined) ?? '');
  }

  async token(): Promise<string> {
    try {
      await this.keycloak.updateToken(30);
    } catch {
      await this.login();
      throw new Error(this.i18n.t('errors.unauthorized'));
    }

    return this.keycloak.token ?? '';
  }

  username(): string {
    return this.user();
  }

  login(): Promise<void> {
    return this.keycloak.login({ redirectUri: window.location.href, locale: this.locale });
  }

  logout(): void {
    void this.keycloak.logout({ redirectUri: document.baseURI });
  }

  accountConsole(): void {
    window.location.assign(this.keycloak.createAccountUrl());
  }
}

/** Bearer token and language for the calls to the Brocal API. */
export const standaloneAuthInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(API)) {
    return next(request);
  }

  const auth = inject(StandaloneAuth);
  const language = inject(TranslocoService).getActiveLang();
  return from(auth.token()).pipe(switchMap((token) => next(request.clone({ setHeaders: { Authorization: `Bearer ${token}`, 'Accept-Language': language } }))));
};

/** Same behavior as the Shell: expired sessions go back to the login, other errors show a message. */
export const standaloneErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(StandaloneAuth);
  const snack = inject(MatSnackBar);
  const i18n = inject(I18n);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 401) {
          void auth.login();
        } else if (!(error.error instanceof Blob)) {
          snack.open(describeError(error, i18n), i18n.t('common.close'), { duration: 8000, panelClass: 'cl-snack--error' });
        } else {
          void error.error
            .text()
            .then((text) => {
              let body: unknown = null;
              try {
                body = JSON.parse(text);
              } catch {
                body = null;
              }

              snack.open(describeError(new HttpErrorResponse({ error: body, status: error.status }), i18n), i18n.t('common.close'), { duration: 8000, panelClass: 'cl-snack--error' });
            })
            .catch(() => undefined);
        }
      }

      return throwError(() => error);
    }),
  );
};
