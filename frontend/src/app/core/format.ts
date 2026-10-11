import { Pipe, PipeTransform } from '@angular/core';
import { activeLocale } from './i18n';
import { IsoDate } from './models';

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

export function money(value: number | null | undefined, currency = 'MXN'): string {
  if (value === null || value === undefined) {
    return '—';
  }

  return new Intl.NumberFormat(activeLocale(), { style: 'currency', currency, minimumFractionDigits: 2 }).format(value);
}

export function num(value: number | null | undefined, maxDigits = 2, minDigits = 0): string {
  if (value === null || value === undefined) {
    return '—';
  }

  return new Intl.NumberFormat(activeLocale(), { maximumFractionDigits: maxDigits, minimumFractionDigits: minDigits }).format(value);
}

/** yyyy-MM-dd (DateOnly) or a full ISO instant, shown as a local date. */
export function date(value: string | null | undefined, style: 'short' | 'medium' | 'long' = 'medium'): string {
  if (!value) {
    return '—';
  }

  const options: Intl.DateTimeFormatOptions =
    style === 'short' ? { day: '2-digit', month: '2-digit' } : style === 'long' ? { dateStyle: 'long' } : { day: '2-digit', month: 'short', year: 'numeric' };
  if (ISO_DATE.test(value)) {
    const [y, m, d] = value.split('-').map(Number);
    return new Intl.DateTimeFormat(activeLocale(), { ...options, timeZone: 'UTC' }).format(Date.UTC(y, m - 1, d));
  }

  return new Intl.DateTimeFormat(activeLocale(), options).format(new Date(value));
}

export function dateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }

  return new Intl.DateTimeFormat(activeLocale(), { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(value));
}

export function time(value: string | null | undefined): string {
  return value ? new Intl.DateTimeFormat(activeLocale(), { hour: '2-digit', minute: '2-digit' }).format(new Date(value)) : '—';
}

/** Today in the device's local calendar (yyyy-MM-dd). */
export function today(): IsoDate {
  return toIsoDate(new Date());
}

export function toIsoDate(value: Date): IsoDate {
  const pad = (n: number, l = 2) => String(n).padStart(l, '0');
  return `${pad(value.getFullYear(), 4)}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

export function addDays(value: IsoDate, days: number): IsoDate {
  const [y, m, d] = value.split('-').map(Number);
  return toIsoDate(new Date(y, m - 1, d + days));
}

export function firstOfMonth(offsetMonths = 0): IsoDate {
  const now = new Date();
  return toIsoDate(new Date(now.getFullYear(), now.getMonth() + offsetMonths, 1));
}

/** `<input type="datetime-local">` value (local time) ↔ ISO instant with offset. */
export function toLocalInput(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  const d = new Date(value);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function fromLocalInput(value: string | null | undefined): string | null {
  return value ? new Date(value).toISOString() : null;
}

export function nowLocalInput(offsetHours = 0): string {
  return toLocalInput(new Date(Date.now() + offsetHours * 3_600_000).toISOString());
}

@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  transform(value: number | null | undefined, currency = 'MXN'): string {
    return money(value, currency);
  }
}

@Pipe({ name: 'num', pure: false })
export class NumPipe implements PipeTransform {
  transform(value: number | null | undefined, maxDigits = 2, minDigits = 0): string {
    return num(value, maxDigits, minDigits);
  }
}

@Pipe({ name: 'd', pure: false })
export class DatePipe implements PipeTransform {
  transform(value: string | null | undefined, style: 'short' | 'medium' | 'long' = 'medium'): string {
    return date(value, style);
  }
}

@Pipe({ name: 'dt', pure: false })
export class DateTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return dateTime(value);
  }
}
