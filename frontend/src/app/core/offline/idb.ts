/** Minimal promise wrapper over IndexedDB for the offline capture of tickets and the lookup cache. */
const DB_NAME = 'brocal-offline';
const DB_VERSION = 1;

export const OUTBOX_STORE = 'outbox';
export const CACHE_STORE = 'cache';

let database: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
  database ??= new Promise<IDBDatabase>((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION);
    request.onupgradeneeded = () => {
      const db = request.result;
      if (!db.objectStoreNames.contains(OUTBOX_STORE)) {
        db.createObjectStore(OUTBOX_STORE, { keyPath: 'clientId' });
      }

      if (!db.objectStoreNames.contains(CACHE_STORE)) {
        db.createObjectStore(CACHE_STORE, { keyPath: 'key' });
      }
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => {
      database = null;
      reject(request.error);
    };
  });
  return database;
}

function run<T>(store: string, mode: IDBTransactionMode, action: (s: IDBObjectStore) => IDBRequest): Promise<T> {
  return open().then(
    (db) =>
      new Promise<T>((resolve, reject) => {
        const request = action(db.transaction(store, mode).objectStore(store));
        request.onsuccess = () => resolve(request.result as T);
        request.onerror = () => reject(request.error);
      }),
  );
}

export function idbGet<T>(store: string, key: string): Promise<T | undefined> {
  return run<T | undefined>(store, 'readonly', (s) => s.get(key));
}

export function idbAll<T>(store: string): Promise<T[]> {
  return run<T[]>(store, 'readonly', (s) => s.getAll());
}

export function idbPut<T>(store: string, value: T): Promise<unknown> {
  return run(store, 'readwrite', (s) => s.put(value));
}

export function idbDelete(store: string, key: string): Promise<unknown> {
  return run(store, 'readwrite', (s) => s.delete(key));
}
