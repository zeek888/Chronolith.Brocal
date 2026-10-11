import { ChangeDetectionStrategy, Component, Injector, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable, debounceTime, firstValueFrom, map } from 'rxjs';
import { BrocalApi, Query, saveFile } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';
import { Lookups } from '../../core/lookups';
import { PagedResult } from '../../core/models';
import { BrocalNav } from '../../core/nav';
import { apiResource } from '../../core/resource';
import { BrocalSession, CanDirective } from '../../core/session';
import { Column, DataTable } from '../data-table';
import { Dialogs } from '../dialogs';
import { LookupSelect } from '../lookup-select';
import { PageHeader } from '../ui';
import { EntityDialog, EntityDialogData } from './entity-dialog';
import { FieldDef } from './fields';

export interface ListFilter {
  key: string;
  label: string;
  type: 'search' | 'enum' | 'lookup' | 'date' | 'toggle';
  enum?: string;
  values?: readonly string[];
  catalog?: string;
  multiple?: boolean;
  default?: unknown;
}

/** Helpers handed to the actions of lists and records. */
export interface ActionContext {
  api: BrocalApi;
  dialogs: Dialogs;
  i18n: I18n;
  nav: BrocalNav;
  session: BrocalSession;
  injector: Injector;
  /** Opens a form dialog; resolves with the saved result (null when cancelled). */
  form: (spec: DialogSpec, value?: Record<string, unknown> | null) => Promise<unknown>;
}

export interface DialogSpec {
  /** Title already in the active language (a folio, a record name, an `i18n.t(...)`). */
  title?: string;
  /** Translation key of the title, for dialogs declared as constants (no language at that point). */
  titleKey?: string;
  fields: FieldDef[];
  defaults?: () => Record<string, unknown>;
  save: (api: BrocalApi, value: Record<string, unknown>) => Observable<unknown>;
  width?: string;
}

export interface RowAction<T> {
  key: string;
  icon: string;
  label: string;
  permissions?: readonly string[];
  visible?: (row: T) => boolean;
  /** Returns true when the list must reload. */
  run: (ctx: ActionContext, row: T) => Promise<boolean | void>;
}

export interface HeaderAction {
  key: string;
  icon: string;
  label: string;
  permissions?: readonly string[];
  module?: string;
  /** Route (relative to Brocal) or an action. */
  path?: string;
  run?: (ctx: ActionContext, filters: Record<string, unknown>) => Promise<boolean | void>;
  primary?: boolean;
}

export interface ListConfig<T = Record<string, unknown>> {
  key: string;
  icon: string;
  api: string;
  columns: Column<T>[];
  filters?: ListFilter[];
  /** Detail page of a row (segments relative to Brocal). */
  link?: (row: T) => (string | null | undefined)[];
  /** New record: page or dialog. */
  create?: { permissions: readonly string[]; path?: string; dialog?: DialogSpec; label?: string };
  headerActions?: HeaderAction[];
  rowActions?: RowAction<T>[];
  /** The endpoint returns an array instead of a page. */
  paged?: boolean;
  pageSize?: number;
  /** Excel export through the integrations datasets. */
  exportDataset?: string;
  muted?: (row: T) => boolean;
  /** Fixed query values (e.g. a subject). */
  query?: Query;
}

export function actionContext(injector: Injector): ActionContext {
  const api = injector.get(BrocalApi);
  const dialog = injector.get(MatDialog);
  const i18n = injector.get(I18n);
  return {
    api,
    dialogs: injector.get(Dialogs),
    i18n,
    nav: injector.get(BrocalNav),
    session: injector.get(BrocalSession),
    injector,
    form: (spec, value) => {
      const data: EntityDialogData = {
        title: spec.title ?? (spec.titleKey ? i18n.t(spec.titleKey) : ''),
        fields: spec.fields,
        value: value ?? spec.defaults?.() ?? null,
        record: value ?? null,
        editing: !!value,
        save: (form) => spec.save(api, form),
      };
      return firstValueFrom(dialog.open(EntityDialog, { data, width: spec.width ?? '760px', maxWidth: '96vw' }).afterClosed()).then((r: unknown) => r ?? null);
    },
  };
}

/**
 * List page driven by a {@link ListConfig}: filters (search, enums, catalogs, dates, toggles), server paging,
 * links to the detail, creation by page or dialog, row actions and Excel export.
 */
@Component({
  selector: 'br-list-page',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatSelectModule,
    MatSlideToggleModule,
    TPipe,
    CanDirective,
    DataTable,
    LookupSelect,
    PageHeader,
  ],
  template: `
    <div class="cl-page">
      <br-page-header [title]="'nav.' + config().key | t" [icon]="config().icon" [subtitle]="'lists.' + config().key + 'Hint' | t">
        @for (action of headerActions(); track action.key) {
          @if (action.path) {
            @if (action.primary) {
              <a mat-flat-button [routerLink]="nav.to(action.path)"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</a>
            } @else {
              <a mat-stroked-button [routerLink]="nav.to(action.path)"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</a>
            }
          } @else {
            <button mat-stroked-button type="button" (click)="runHeader(action)"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</button>
          }
        }
        @if (config().exportDataset && canExport()) {
          <button mat-stroked-button type="button" (click)="export()"><mat-icon>download</mat-icon>{{ 'common.excel' | t }}</button>
        }
        @if (config().create; as create) {
          <ng-container *brCan="create.permissions">
            @if (create.path) {
              <a mat-flat-button [routerLink]="nav.to(create.path)"><mat-icon>add</mat-icon>{{ (create.label ?? 'common.new') | t }}</a>
            } @else {
              <button mat-flat-button type="button" (click)="createWithDialog()"><mat-icon>add</mat-icon>{{ (create.label ?? 'common.new') | t }}</button>
            }
          </ng-container>
        }
      </br-page-header>

      @if ((config().filters ?? []).length > 0) {
        <div class="cl-toolbar-row br-filters">
          @for (filter of config().filters ?? []; track filter.key) {
            @switch (filter.type) {
              @case ('search') {
                <mat-form-field class="cl-search" subscriptSizing="dynamic">
                  <mat-icon matPrefix>search</mat-icon>
                  <mat-label>{{ filter.label | t }}</mat-label>
                  <input matInput [ngModel]="search()" (ngModelChange)="search.set($event); page.set(1)" />
                </mat-form-field>
              }
              @case ('enum') {
                <mat-form-field class="br-filter" subscriptSizing="dynamic">
                  <mat-label>{{ filter.label | t }}</mat-label>
                  <mat-select [multiple]="!!filter.multiple" [ngModel]="values()[filter.key] ?? (filter.multiple ? [] : null)" (ngModelChange)="set(filter.key, $event)">
                    @if (!filter.multiple) {
                      <mat-option [value]="null">{{ 'common.all' | t }}</mat-option>
                    }
                    @for (value of filter.values ?? []; track value) {
                      <mat-option [value]="value">{{ 'enums.' + filter.enum + '.' + value | t }}</mat-option>
                    }
                  </mat-select>
                </mat-form-field>
              }
              @case ('lookup') {
                <br-lookup class="br-filter br-filter--lookup" [catalog]="filter.catalog!" [label]="filter.label | t" [value]="$any(values()[filter.key]) ?? null" (selected)="set(filter.key, idOf($event))" />
              }
              @case ('date') {
                <mat-form-field class="br-filter" subscriptSizing="dynamic">
                  <mat-label>{{ filter.label | t }}</mat-label>
                  <input matInput [matDatepicker]="picker" [ngModel]="values()[filter.key] ?? null" (ngModelChange)="set(filter.key, $event)" />
                  <mat-datepicker-toggle matIconSuffix [for]="picker" />
                  <mat-datepicker #picker />
                </mat-form-field>
              }
              @case ('toggle') {
                <mat-slide-toggle [ngModel]="!!values()[filter.key]" (ngModelChange)="set(filter.key, $event || null)">{{ filter.label | t }}</mat-slide-toggle>
              }
            }
          }
        </div>
      }

      <br-table
        [columns]="config().columns"
        [rows]="rows()"
        [loading]="data.isLoading()"
        [total]="total()"
        [page]="page()"
        [pageSize]="pageSize()"
        [paged]="config().paged !== false"
        [link]="link()"
        [muted]="config().muted ?? null"
        (pageChange)="page.set($event.page); pageSize.set($event.pageSize)"
      >
        @if (rowActions().length > 0) {
          <ng-template #actions let-row>
            @if (visibleActions(row).length > 0) {
              <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="'common.actions' | t"><mat-icon>more_vert</mat-icon></button>
              <mat-menu #menu="matMenu">
                @for (action of visibleActions(row); track action.key) {
                  <button mat-menu-item type="button" (click)="runRow(action, row)"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</button>
                }
              </mat-menu>
            }
          </ng-template>
        }
      </br-table>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ListPage {
  private readonly api = inject(BrocalApi);
  private readonly injector = inject(Injector);
  private readonly route = inject(ActivatedRoute);
  private readonly lookups = inject(Lookups);
  protected readonly nav = inject(BrocalNav);
  protected readonly session = inject(BrocalSession);

  readonly list = input<ListConfig | null>(null);
  protected readonly config = computed(() => this.list() ?? (this.route.snapshot.data['list'] as ListConfig));

  protected readonly search = signal('');
  protected readonly values = signal<Record<string, unknown>>(this.initialValues());
  protected readonly page = signal(1);
  protected readonly pageSize = signal(this.config().pageSize ?? 25);
  private readonly reload = signal(0);
  private readonly debounced = toSignal(toObservable(this.search).pipe(debounceTime(300), map((s) => s.trim())), { initialValue: '' });

  protected readonly data = apiResource({
    params: () => ({ ...this.config().query, ...this.values(), search: this.debounced() || null, page: this.page(), pageSize: this.pageSize(), reload: this.reload() }),
    stream: ({ params }) => {
      const { reload: _, ...query } = params;
      const config = this.config();
      return config.paged === false
        ? this.api.get<unknown[]>(config.api, query as Query).pipe(map((items) => ({ items, totalCount: items.length }) as Pick<PagedResult<unknown>, 'items' | 'totalCount'>))
        : this.api.get<PagedResult<unknown>>(config.api, query as Query);
    },
  });

  protected readonly rows = computed(() => (this.data.value()?.items ?? []) as Record<string, unknown>[]);
  protected readonly total = computed(() => this.data.value()?.totalCount ?? 0);
  protected readonly link = computed(() => {
    const link = this.config().link;
    return link ? (row: Record<string, unknown>) => this.nav.to(...link(row)) : null;
  });
  protected readonly rowActions = computed(() => (this.config().rowActions ?? []).filter((a) => !a.permissions || this.session.can(...a.permissions)));
  protected readonly headerActions = computed(() =>
    (this.config().headerActions ?? []).filter((a) => (!a.permissions || this.session.can(...a.permissions)) && (!a.module || this.session.hasModule(a.module))),
  );
  protected readonly canExport = computed(() => this.session.hasModule('integrations') && this.session.can('integrations.export', 'integrations.manage'));

  private initialValues(): Record<string, unknown> {
    const values: Record<string, unknown> = {};
    const query = this.route.snapshot.queryParamMap;
    for (const filter of this.config().filters ?? []) {
      const fromUrl = query.get(filter.key);
      if (fromUrl !== null) {
        values[filter.key] = filter.type === 'toggle' ? fromUrl === 'true' : fromUrl;
      } else if (filter.default !== undefined) {
        values[filter.key] = filter.default;
      }
    }

    return values;
  }

  protected set(key: string, value: unknown): void {
    this.values.update((v) => ({ ...v, [key]: Array.isArray(value) && value.length === 0 ? null : value }));
    this.page.set(1);
  }

  protected idOf(selected: unknown): string | null {
    return (selected as { id?: string } | null)?.id ?? null;
  }

  protected visibleActions(row: Record<string, unknown>): RowAction<Record<string, unknown>>[] {
    return this.rowActions().filter((a) => !a.visible || a.visible(row));
  }

  protected async runRow(action: RowAction<Record<string, unknown>>, row: Record<string, unknown>): Promise<void> {
    if ((await action.run(actionContext(this.injector), row)) !== false) {
      this.refresh();
    }
  }

  protected async runHeader(action: HeaderAction): Promise<void> {
    if (action.run && (await action.run(actionContext(this.injector), this.values())) !== false) {
      this.refresh();
    }
  }

  protected async createWithDialog(): Promise<void> {
    const spec = this.config().create?.dialog;
    if (!spec) {
      return;
    }

    const ctx = actionContext(this.injector);
    if (await ctx.form(spec)) {
      ctx.dialogs.success(ctx.i18n.t('common.saved'));
      this.refresh();
    }
  }

  protected export(): void {
    const values = this.values();
    this.api
      .download(`integrations/exports/${this.config().exportDataset}`, { from: (values['from'] as string) ?? null, to: (values['to'] as string) ?? null })
      .subscribe((file) => saveFile(file));
  }

  private refresh(): void {
    this.lookups.invalidate();
    this.reload.update((r) => r + 1);
  }
}
