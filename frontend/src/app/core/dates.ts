import { Injectable, effect, inject } from '@angular/core';
import { DateAdapter, MatDateFormats, NativeDateAdapter } from '@angular/material/core';
import { MatDatepickerIntl } from '@angular/material/datepicker';
import { I18n, activeLocale } from './i18n';

const ISO = /^\d{4}-\d{2}-\d{2}$/;
const INVALID = 'invalid';

/** Order of day and month when typing a date in the locale (es-MX: dd/MM/yyyy, en-US: MM/dd/yyyy). */
function dateOrder(locale: string): 'dmy' | 'mdy' {
  const parts = new Intl.DateTimeFormat(locale).formatToParts(new Date(2000, 10, 22));
  const order = parts.filter((p) => p.type === 'day' || p.type === 'month').map((p) => p.type);
  return order[0] === 'month' ? 'mdy' : 'dmy';
}

/**
 * Datepicker adapter working directly with ISO dates (yyyy-MM-dd), the format of the API's DateOnly, so
 * form values go to the API unchanged. Dates are shown and typed in the order of the active language.
 */
@Injectable()
export class IsoDateAdapter extends DateAdapter<string> {
  private readonly native = new NativeDateAdapter();

  constructor() {
    super();
    effect(() => this.setLocale(activeLocale()));
  }

  override setLocale(locale: string): void {
    this.native.setLocale(locale);
    super.setLocale(locale);
  }

  private get localeCode(): string {
    return typeof this.locale === 'string' ? this.locale : activeLocale();
  }

  private toDate(value: string): Date {
    const [y, m, d] = value.split('-').map(Number);
    const result = new Date(y, m - 1, d);
    result.setFullYear(y);
    return result;
  }

  private toIso(value: Date): string {
    if (isNaN(value.getTime())) {
      return INVALID;
    }

    const pad = (n: number, l = 2) => String(n).padStart(l, '0');
    return `${pad(value.getFullYear(), 4)}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
  }

  getYear(value: string): number {
    return this.toDate(value).getFullYear();
  }

  getMonth(value: string): number {
    return this.toDate(value).getMonth();
  }

  getDate(value: string): number {
    return this.toDate(value).getDate();
  }

  getDayOfWeek(value: string): number {
    return this.toDate(value).getDay();
  }

  getMonthNames(style: 'long' | 'short' | 'narrow'): string[] {
    return this.native.getMonthNames(style);
  }

  getDateNames(): string[] {
    return this.native.getDateNames();
  }

  getDayOfWeekNames(style: 'long' | 'short' | 'narrow'): string[] {
    return this.native.getDayOfWeekNames(style);
  }

  getYearName(value: string): string {
    return String(this.getYear(value));
  }

  getFirstDayOfWeek(): number {
    return 0;
  }

  getNumDaysInMonth(value: string): number {
    const d = this.toDate(value);
    return new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
  }

  clone(value: string): string {
    return value;
  }

  createDate(year: number, month: number, date: number): string {
    const d = new Date(year, month, date);
    d.setFullYear(year);
    return this.toIso(d);
  }

  today(): string {
    return this.toIso(new Date());
  }

  parse(value: unknown): string | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    if (value instanceof Date) {
      return this.toIso(value);
    }

    const text = String(value).trim();
    if (ISO.test(text)) {
      return this.isValid(text) ? text : INVALID;
    }

    const match = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2}|\d{4})$/.exec(text);
    if (!match) {
      return INVALID;
    }

    const [first, second] = [Number(match[1]), Number(match[2])];
    const [day, month] = dateOrder(this.localeCode) === 'mdy' ? [second, first] : [first, second];
    const year = match[3].length === 2 ? 2000 + Number(match[3]) : Number(match[3]);
    const iso = this.createDate(year, month - 1, day);
    return this.getDate(iso) === day && this.getMonth(iso) === month - 1 ? iso : INVALID;
  }

  format(value: string, displayFormat: unknown): string {
    if (!this.isValid(value)) {
      return '';
    }

    if (displayFormat === 'input') {
      const [y, m, d] = value.split('-');
      return dateOrder(this.localeCode) === 'mdy' ? `${m}/${d}/${y}` : `${d}/${m}/${y}`;
    }

    return new Intl.DateTimeFormat(this.localeCode, { ...(displayFormat as Intl.DateTimeFormatOptions), timeZone: 'UTC' }).format(
      Date.UTC(this.getYear(value), this.getMonth(value), this.getDate(value)),
    );
  }

  addCalendarYears(value: string, years: number): string {
    return this.toIso(this.native.addCalendarYears(this.toDate(value), years));
  }

  addCalendarMonths(value: string, months: number): string {
    return this.toIso(this.native.addCalendarMonths(this.toDate(value), months));
  }

  addCalendarDays(value: string, days: number): string {
    return this.toIso(this.native.addCalendarDays(this.toDate(value), days));
  }

  toIso8601(value: string): string {
    return value;
  }

  isDateInstance(obj: unknown): boolean {
    return typeof obj === 'string' && (obj === INVALID || ISO.test(obj));
  }

  isValid(value: string): boolean {
    return value !== INVALID && ISO.test(value) && !isNaN(this.toDate(value).getTime());
  }

  invalid(): string {
    return INVALID;
  }

  override deserialize(value: unknown): string | null {
    if (value === '' || value === null || value === undefined) {
      return null;
    }

    if (typeof value === 'string' && ISO.test(value)) {
      return value;
    }

    return super.deserialize(value);
  }
}

export const ISO_DATE_FORMATS: MatDateFormats = {
  parse: { dateInput: 'input' },
  display: {
    dateInput: 'input',
    monthLabel: { month: 'short' },
    monthYearLabel: { year: 'numeric', month: 'short' },
    dateA11yLabel: { year: 'numeric', month: 'long', day: 'numeric' },
    monthYearA11yLabel: { year: 'numeric', month: 'long' },
  },
};

/** Calendar texts and tooltips in the active language. */
@Injectable()
export class TranslatedDatepickerIntl extends MatDatepickerIntl {
  constructor() {
    super();
    const i18n = inject(I18n);
    effect(() => {
      this.calendarLabel = i18n.t('calendar.calendar');
      this.openCalendarLabel = i18n.t('calendar.open');
      this.closeCalendarLabel = i18n.t('calendar.close');
      this.prevMonthLabel = i18n.t('calendar.prevMonth');
      this.nextMonthLabel = i18n.t('calendar.nextMonth');
      this.prevYearLabel = i18n.t('calendar.prevYear');
      this.nextYearLabel = i18n.t('calendar.nextYear');
      this.prevMultiYearLabel = i18n.t('calendar.prevYears');
      this.nextMultiYearLabel = i18n.t('calendar.nextYears');
      this.switchToMonthViewLabel = i18n.t('calendar.chooseDate');
      this.switchToMultiYearViewLabel = i18n.t('calendar.chooseMonthYear');
      this.startDateLabel = i18n.t('calendar.start');
      this.endDateLabel = i18n.t('calendar.end');
      this.changes.next();
    });
  }
}
