import { Injectable, inject } from '@angular/core';
import { Observable, catchError, from, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { BrocalApi, Query, TenantHeader } from './api';
import { Lookup } from './models';
import { CACHE_STORE, idbGet, idbPut } from './offline/idb';

interface CacheEntry<T> {
  key: string;
  value: T;
  savedAt: string;
}

/**
 * Selectors data (customers, sites, units, services…). Answers are kept in memory for the session and in
 * IndexedDB per company, so the field capture keeps working without signal.
 */
@Injectable({ providedIn: 'root' })
export class Lookups {
  private readonly api = inject(BrocalApi);
  private readonly tenant = inject(TenantHeader);
  private readonly memory = new Map<string, Observable<unknown>>();

  /** `catalogs/customers` → GET catalogs/customers/lookup. */
  list(catalog: string, query?: Query): Observable<Lookup[]> {
    return this.cached<Lookup[]>(`${catalog}/lookup`, { activeOnly: true, ...query });
  }

  /** Any GET whose answer should survive offline (service type forms, prices). */
  cached<T>(path: string, query?: Query): Observable<T> {
    const key = `${this.tenant.slug() ?? '-'}|${path}|${JSON.stringify(query ?? {})}`;
    let request = this.memory.get(key) as Observable<T> | undefined;
    if (!request) {
      request = this.api.get<T>(path, query).pipe(
        tap((value) => void idbPut<CacheEntry<T>>(CACHE_STORE, { key, value, savedAt: new Date().toISOString() }).catch(() => undefined)),
        catchError((error: unknown) =>
          from(idbGet<CacheEntry<T>>(CACHE_STORE, key).catch(() => undefined)).pipe(
            switchMap((entry) => (entry ? of(entry.value) : throwError(() => error))),
          ),
        ),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.memory.set(key, request);
    }

    return request;
  }

  /** After creating or editing catalog records. */
  invalidate(prefix?: string): void {
    for (const key of [...this.memory.keys()]) {
      if (!prefix || key.includes(`|${prefix}`)) {
        this.memory.delete(key);
      }
    }
  }
}
