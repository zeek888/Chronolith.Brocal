import { Injectable, inject, signal } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationExtras, Router, UrlTree } from '@angular/router';

/** Data flag of the root route of Brocal (the layout); its URL is the base of every link of the module. */
export const BROCAL_ROOT = 'brocalRoot';

/** URL segments from the application root to the Brocal root route (['brocal'] inside ChronoLith, [] standalone). */
export function baseOf(route: ActivatedRouteSnapshot): string[] {
  const chain = route.pathFromRoot;
  const rootIndex = chain.findIndex((r) => r.data[BROCAL_ROOT]);
  const upTo = rootIndex < 0 ? chain : chain.slice(0, rootIndex + 1);
  return upTo.flatMap((r) => r.url.map((s) => s.path));
}

/**
 * Links of the module. Inside ChronoLith Brocal is mounted under `/<routePath>` (configurable by the Shell
 * administrator), so links are built from the base where the module was actually mounted, never `/ruta`.
 */
@Injectable({ providedIn: 'root' })
export class BrocalNav {
  private readonly router = inject(Router);

  readonly base = signal<string[]>([]);

  /** Router link array: `nav.to('boletas', id)`. */
  to(...segments: (string | number | null | undefined)[]): string[] {
    const parts = segments.filter((s) => s !== null && s !== undefined && s !== '').flatMap((s) => String(s).split('/').filter(Boolean));
    return ['/', ...this.base(), ...parts];
  }

  /** Absolute URL of a relative link sent by the API (notifications, history): `boletas/{id}`. */
  url(link: string): string {
    return '/' + [...this.base(), ...link.split('/').filter(Boolean)].join('/');
  }

  navigate(segments: (string | number | null | undefined)[], extras?: NavigationExtras): Promise<boolean> {
    return this.router.navigate(this.to(...segments), extras);
  }

  navigateByLink(link: string): Promise<boolean> {
    return this.router.navigateByUrl(this.url(link));
  }

  home(route?: ActivatedRouteSnapshot): UrlTree {
    const base = route ? baseOf(route) : this.base();
    return this.router.createUrlTree(['/', ...base, 'inicio']);
  }
}
