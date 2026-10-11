import { BreakpointObserver } from '@angular/cdk/layout';
import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { BROCAL_MODE, RUNTIME_CONFIG } from '../core/config';
import { DateTimePipe } from '../core/format';
import { I18n, TPipe } from '../core/i18n';
import { MENU, MenuGroup, MenuItem } from '../core/menu';
import { BrocalNav, baseOf } from '../core/nav';
import { BROCAL_AUTH, InboxService } from '../core/notifications';
import { TicketOutbox } from '../core/offline/ticket-outbox';
import { BrocalSession } from '../core/session';
import { BrocalTheme, THEMED_CLASS } from '../core/theme/brocal-theme';
import { fileUrl } from '../core/config';
import { EmptyState } from '../shared/ui';

/**
 * Root of every Brocal page. Inside ChronoLith it is a band under the Shell toolbar with the company
 * switcher, the grouped menu of the 24 modules, the offline queue and the inbox; standalone it is the whole
 * application chrome (side navigation, toolbar, session). Global styles of the module live here
 * (`ViewEncapsulation.None`), so they also apply to dialogs and respect the theme of the host.
 */
@Component({
  selector: 'br-layout',
  imports: [
    NgTemplateOutlet,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatBadgeModule,
    MatButtonModule,
    MatDividerModule,
    MatIconModule,
    MatListModule,
    MatMenuModule,
    MatProgressBarModule,
    MatSidenavModule,
    MatToolbarModule,
    MatTooltipModule,
    TPipe,
    DateTimePipe,
    EmptyState,
  ],
  templateUrl: './brocal-layout.html',
  styleUrl: './brocal.scss',
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'hostClass()' },
})
export class BrocalLayout implements OnInit, OnDestroy {
  protected readonly session = inject(BrocalSession);
  protected readonly theme = inject(BrocalTheme);
  protected readonly nav = inject(BrocalNav);
  protected readonly inbox = inject(InboxService);
  protected readonly outbox = inject(TicketOutbox);
  protected readonly i18n = inject(I18n);
  protected readonly standalone = inject(BROCAL_MODE) === 'standalone';
  protected readonly auth = inject(BROCAL_AUTH, { optional: true });
  protected readonly chronolithUrl = inject(RUNTIME_CONFIG, { optional: true })?.chronolithUrl ?? null;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly handset = toSignal(inject(BreakpointObserver).observe('(max-width: 960px)').pipe(map((r) => r.matches)), {
    initialValue: typeof window !== 'undefined' && window.matchMedia('(max-width: 960px)').matches,
  });
  protected readonly sidenavOpen = signal(!this.handset());

  protected readonly hostClass = computed(() => ['br-root', THEMED_CLASS, this.standalone ? 'br-standalone' : 'br-module'].join(' '));

  protected readonly menu = computed<MenuGroup[]>(() => {
    const session = this.session;
    session.permissions();
    const visible = (item: MenuItem) =>
      (item.platformAdmin ? session.isPlatformAdmin() : !!session.tenant()) && (!item.module || session.hasModule(item.module)) && (item.platformAdmin || session.can(...item.permissions));
    return MENU.map((group) => ({ ...group, items: group.items.filter(visible) })).filter((group) => group.items.length > 0);
  });

  protected readonly logo = computed(() => fileUrl(this.theme.logoUrl()));
  protected readonly otherTenants = computed(() => this.session.tenants().filter((t) => t.slug !== this.session.tenant()?.slug && t.status !== 'cancelled'));

  /** Group of the current page (highlights the band menu inside ChronoLith). */
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });
  protected readonly activeGroup = computed(() => {
    const url = this.url();
    const base = '/' + this.nav.base().join('/');
    const path = url.startsWith(base) ? url.slice(base.length).replace(/^\/+/, '').split('?')[0] : '';
    return this.menu().find((g) => g.items.some((i) => path === i.path || path.startsWith(`${i.path}/`)))?.key ?? null;
  });

  ngOnInit(): void {
    this.nav.base.set(baseOf(this.route.snapshot));
    if (!this.standalone) {
      this.theme.attachOverlay();
    }

    this.inbox.start();
    void this.outbox.sync();
  }

  ngOnDestroy(): void {
    this.inbox.stop();
    if (!this.standalone) {
      this.theme.detachOverlay();
    }
  }

  protected link(path: string): string[] {
    return this.nav.to(path);
  }

  protected async switchTenant(slug: string): Promise<void> {
    await this.session.switchTenant(slug);
    await this.nav.navigate(['inicio']);
  }

  protected async openNotification(link: string | null, id: string): Promise<void> {
    await this.inbox.markRead([id]);
    if (link) {
      await this.nav.navigateByLink(link);
    }
  }

  protected toggleLanguage(): void {
    this.i18n.setLanguage(this.i18n.language() === 'es' ? 'en' : 'es');
  }

  protected closeOnHandset(): void {
    if (this.handset()) {
      this.sidenavOpen.set(false);
    }
  }
}
