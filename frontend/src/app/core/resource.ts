import { Signal, computed } from '@angular/core';
import { RxResourceOptions, rxResource } from '@angular/core/rxjs-interop';

/** A read of the API shown by a page or widget. */
export interface ApiResource<T> {
  /** The data; undefined while the first load runs or after a failed request. */
  readonly value: Signal<T | undefined>;
  readonly isLoading: Signal<boolean>;
  /** Failure of the last request (the HTTP error handler already told the user). */
  readonly error: Signal<unknown>;
  reload(): void;
}

/**
 * `rxResource` for API reads. Angular resources throw from `value()` while they are in error state, which breaks
 * every template and computed signal that reads them after a failed request (a 403 of a module the company did
 * not contract, a 404…). Here the value is read through `hasValue()`, so a failed request just leaves it empty.
 */
export function apiResource<T, P>(options: RxResourceOptions<T, P>): ApiResource<T> {
  const resource = rxResource(options);
  return {
    value: computed(() => (resource.hasValue() ? resource.value() : undefined)),
    isLoading: resource.isLoading,
    error: resource.error,
    reload: () => void resource.reload(),
  };
}
