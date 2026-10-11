import { Injectable, Pipe, PipeTransform, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoScope, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom, map } from 'rxjs';

/**
 * Texts of Brocal: Transloco scope `brocal` with an inline loader, so the module does not depend on files
 * of the Shell. Inside ChronoLith Transloco is shared as a singleton and Brocal switches language with it.
 */
export const BROCAL_SCOPE = {
  scope: 'brocal',
  loader: {
    es: () => import('../i18n/es.json'),
    en: () => import('../i18n/en.json'),
  },
} satisfies TranslocoScope;

export type TranslateParams = Record<string, unknown>;

const LOCALES: Record<string, string> = { es: 'es-MX', en: 'en-US' };

/** Active locale for dates and numbers (read by the format helpers, so computeds refresh on language change). */
export const activeLocale = signal('es-MX');

@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly transloco = inject(TranslocoService);

  /** Language whose Brocal texts are loaded (null while the first load runs). */
  private readonly loaded = toSignal(
    this.transloco.selectTranslate('', {}, BROCAL_SCOPE).pipe(map(() => this.transloco.getActiveLang())),
    { initialValue: null },
  );

  readonly language = computed(() => this.loaded() ?? this.transloco.getActiveLang());
  readonly locale = computed(() => {
    const language = this.language();
    return LOCALES[language] ?? LOCALES[language.split('-')[0]] ?? language;
  });

  constructor() {
    effect(() => activeLocale.set(this.locale()));
  }

  /** For route guards: waits until the texts of the active language are available. */
  ready(): Promise<boolean> {
    return firstValueFrom(this.transloco.selectTranslate('', {}, BROCAL_SCOPE)).then(() => true);
  }

  /** Text of `brocal.<key>`; reactive to the language inside computeds and templates. */
  t(key: string, params?: TranslateParams): string {
    this.loaded();
    return this.transloco.translate(`${BROCAL_SCOPE.scope}.${key}`, params);
  }

  /**
   * Label of an enum value: `enums.<enumName>.<value>`. The JSON payloads use camelCase, but grouped figures
   * (statistics, counts by status) name the values with the C# casing ("NearMiss"), so both are accepted.
   */
  enum(enumName: string, value: string | null | undefined): string {
    return value ? this.t(`enums.${enumName}.${value.charAt(0).toLowerCase()}${value.slice(1)}`) : '';
  }

  setLanguage(language: string): void {
    this.transloco.setActiveLang(language);
  }
}

/** `{{ 'nav.tickets' | t }}`, `{{ 'common.daysValue' | t: { n: 3 } }}`. Impure: follows the language signal. */
@Pipe({ name: 't', pure: false })
export class TPipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(key: string, params?: TranslateParams): string {
    return this.i18n.t(key, params);
  }
}

/** `{{ ticket.status | enum: 'ticketStatus' }}`. */
@Pipe({ name: 'enum', pure: false })
export class EnumPipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(value: string | null | undefined, enumName: string): string {
    return this.i18n.enum(enumName, value);
  }
}
