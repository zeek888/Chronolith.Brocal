import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute } from '@angular/router';
import { debounceTime, firstValueFrom, map } from 'rxjs';
import { BrocalApi, Query } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';
import { Lookups } from '../../core/lookups';
import { PagedResult } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { DataTable } from '../data-table';
import { Dialogs } from '../dialogs';
import { PageHeader } from '../ui';
import { EntityDialog, EntityDialogData } from './entity-dialog';
import { CatalogConfig } from './fields';

type Row = Record<string, unknown> & { id: string; isActive?: boolean; name?: string; code?: string };

/**
 * Master data page driven by a {@link CatalogConfig}: search, active filter, extra filters, server paging,
 * create/edit dialog, (de)activation and deletion (409 when the record is referenced).
 * `…/:id` opens the record directly (links from notifications).
 */
@Component({
  selector: 'br-catalog-page',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatMenuModule, MatSelectModule, TPipe, CanDirective, DataTable, PageHeader],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.' + config().key | t" [icon]="config().icon" [subtitle]="'catalogs.' + config().key + 'Hint' | t">
        <button *brCan="config().manage" mat-flat-button type="button" (click)="create()"><mat-icon>add</mat-icon>{{ 'common.new' | t }}</button>
      </br-page-header>

      <div class="cl-toolbar-row br-filters">
        <mat-form-field class="cl-search" subscriptSizing="dynamic">
          <mat-icon matPrefix>search</mat-icon>
          <mat-label>{{ 'common.search' | t }}</mat-label>
          <input matInput [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
        </mat-form-field>
        <mat-form-field subscriptSizing="dynamic" class="br-filter">
          <mat-label>{{ 'common.status' | t }}</mat-label>
          <mat-select [ngModel]="active()" (ngModelChange)="active.set($event); page.set(1)">
            <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
            <mat-option [value]="true">{{ 'common.active' | t }}</mat-option>
            <mat-option [value]="false">{{ 'common.inactive' | t }}</mat-option>
          </mat-select>
        </mat-form-field>
        @for (filter of config().filters ?? []; track filter.key) {
          <mat-form-field subscriptSizing="dynamic" class="br-filter">
            <mat-label>{{ filter.label | t }}</mat-label>
            <mat-select [ngModel]="filters()[filter.key] ?? null" (ngModelChange)="setFilter(filter.key, $event)">
              <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
              @for (value of filter.values ?? []; track value) {
                <mat-option [value]="value">{{ 'enums.' + filter.enum + '.' + value | t }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
      </div>

      <br-table
        [columns]="config().columns"
        [rows]="rows()"
        [loading]="data.isLoading()"
        [total]="total()"
        [page]="page()"
        [pageSize]="pageSize()"
        [link]="detailLink()"
        [clickable]="!config().detail"
        [muted]="inactive"
        (activate)="edit($event)"
        (pageChange)="page.set($event.page); pageSize.set($event.pageSize)"
      >
        <ng-template #actions let-row>
          @if (canManage()) {
            <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t"><mat-icon>more_vert</mat-icon></button>
            <mat-menu #menu="matMenu">
              <button mat-menu-item type="button" (click)="edit(row)"><mat-icon>edit</mat-icon>{{ 'common.edit' | t }}</button>
              <button mat-menu-item type="button" (click)="toggleActive(row)">
                <mat-icon>{{ row.isActive ? 'toggle_off' : 'toggle_on' }}</mat-icon>{{ (row.isActive ? 'common.deactivate' : 'common.activate') | t }}
              </button>
              <button mat-menu-item type="button" (click)="remove(row)"><mat-icon>delete</mat-icon>{{ 'common.delete' | t }}</button>
            </mat-menu>
          }
        </ng-template>
      </br-table>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogPage implements OnInit {
  private readonly api = inject(BrocalApi);
  private readonly nav = inject(BrocalNav);
  private readonly dialog = inject(MatDialog);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly lookups = inject(Lookups);
  private readonly session = inject(BrocalSession);
  private readonly route = inject(ActivatedRoute);

  /** From the route data (`data: { catalog }`). */
  readonly catalog = input<CatalogConfig | null>(null);
  readonly id = input<string | null>(null);

  protected readonly config = computed(() => this.catalog() ?? (this.route.snapshot.data['catalog'] as CatalogConfig));
  protected readonly search = signal('');
  protected readonly active = signal<boolean | null>(true);
  protected readonly filters = signal<Record<string, string | null>>({});
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  private readonly reload = signal(0);

  private readonly debouncedSearch = toSignal(toObservable(this.search).pipe(debounceTime(300), map((s) => s.trim())), { initialValue: '' });

  protected readonly data = apiResource({
    params: () => ({
      search: this.debouncedSearch(),
      isActive: this.active(),
      page: this.page(),
      pageSize: this.pageSize(),
      ...this.filters(),
      reload: this.reload(),
    }),
    stream: ({ params }) => {
      const { reload: _, ...query } = params;
      return this.api.get<PagedResult<Row>>(this.config().api, query as Query);
    },
  });

  protected readonly rows = computed(() => this.data.value()?.items ?? []);
  protected readonly total = computed(() => this.data.value()?.totalCount ?? 0);
  protected readonly canManage = computed(() => this.session.can(...this.config().manage));
  /** Segments from the module base (a plain array would resolve against the catalog's own route). */
  protected readonly detailLink = computed(() => {
    const detail = this.config().detail;
    return detail ? (row: Row) => this.nav.to(...detail(row)) : null;
  });
  protected readonly inactive = (row: Row) => row.isActive === false;

  ngOnInit(): void {
    const id = this.id() ?? (this.route.snapshot.paramMap.get('id') as string | null);
    if (id) {
      this.api.get<Row>(`${this.config().api}/${id}`).subscribe((row) => this.edit(row));
    }
  }

  protected setFilter(key: string, value: string | null): void {
    this.filters.update((f) => ({ ...f, [key]: value }));
    this.page.set(1);
  }

  protected create(): void {
    const config = this.config();
    void this.open(null, config.defaults?.() ?? null);
  }

  protected edit(row: Row): void {
    if (!this.canManage()) {
      return;
    }

    const config = this.config();
    void this.open(row, config.toForm ? config.toForm(row) : row);
  }

  private async open(row: Row | null, value: Record<string, unknown> | null): Promise<void> {
    const config = this.config();
    const singular = this.i18n.t(`catalogs.${config.key}`);
    const data: EntityDialogData = {
      title: row ? `${singular} · ${row.name ?? row.code ?? ''}` : this.i18n.t('common.newRecord', { name: singular }),
      fields: config.fields,
      value,
      record: row,
      editing: !!row,
      save: (form) => {
        const payload = config.toPayload ? config.toPayload(form) : form;
        return row ? this.api.put(`${config.api}/${row.id}`, payload) : this.api.post(config.api, payload);
      },
    };

    const saved = await firstValueFrom(this.dialog.open(EntityDialog, { data, width: config.dialogWidth ?? '760px', maxWidth: '96vw' }).afterClosed());
    if (saved) {
      this.afterChange();
      this.dialogs.success(this.i18n.t('common.saved'));
    }
  }

  protected toggleActive(row: Row): void {
    this.api.put(`${this.config().api}/${row.id}/active`, { isActive: !row.isActive }).subscribe(() => this.afterChange());
  }

  protected async remove(row: Row): Promise<void> {
    const ok = await this.dialogs.confirm(this.i18n.t('common.deleteTitle'), this.i18n.t('common.deleteMessage', { name: row.name ?? row.code ?? '' }), this.i18n.t('common.delete'), true);
    if (ok) {
      this.api.delete(`${this.config().api}/${row.id}`).subscribe(() => {
        this.afterChange();
        this.dialogs.success(this.i18n.t('common.deleted'));
      });
    }
  }

  private afterChange(): void {
    const config = this.config();
    for (const catalog of [config.api, ...(config.invalidates ?? [])]) {
      this.lookups.invalidate(catalog);
    }

    this.reload.update((r) => r + 1);
  }
}
