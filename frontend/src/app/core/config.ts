import { InjectionToken } from '@angular/core';

/** `module`: mounted by the ChronoLith Shell (Native Federation). `standalone`: own login, layout and theme. */
export type BrocalMode = 'module' | 'standalone';

export const BROCAL_MODE = new InjectionToken<BrocalMode>('BROCAL_MODE', { providedIn: 'root', factory: () => 'module' });

/**
 * Absolute URL of the Brocal API, on the origin the module was downloaded from (its nginx or the dev
 * server forward /api to the backend). Inside the Shell the code runs on the Shell's origin, so a relative
 * `/api` would reach the Shell API; with the absolute URL the Shell recognizes the `apiBaseUrl` registered
 * in the manifest and adds the Keycloak token.
 */
export const API = new URL('api', import.meta.url).href;

export function apiUrl(path: string): string {
  return `${API}/${path.replace(/^\/+/, '')}`;
}

/** Signed links returned by the API (`files/{id}/content?...`) are relative to the API base. */
export function fileUrl(relative: string | null | undefined): string | null {
  return relative ? new URL(relative, `${API}/`).href : null;
}

/** Settings of the standalone application, written to /config.json by the container entrypoint. */
export interface RuntimeConfig {
  keycloak: { url: string; realm: string; clientId: string };
  /** URL of ChronoLith (link "Abrir en ChronoLith"); empty when Brocal runs on its own. */
  chronolithUrl?: string | null;
}

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('config.json', { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`No se pudo cargar config.json (${response.status})`);
  }

  return (await response.json()) as RuntimeConfig;
}
