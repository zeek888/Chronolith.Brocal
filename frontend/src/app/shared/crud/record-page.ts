import { ChangeDetectionStrategy, Component, Injector, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, ParamMap, RouterLink } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { BrocalApi } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';
import { BrocalNav } from '../../core/nav';
import { BrocalSession } from '../../core/session';
import { Dialogs } from '../dialogs';
import { PageHeader, StatusChip } from '../ui';
import { EntityForm } from './entity-form';
import { FieldDef, buildForm, readForm } from './fields';
import { ActionContext, actionContext } from './list-page';

type Rec = Record<string, unknown>;

export interface RecordAction {
  key: string;
  icon: string;
  label: string;
  permissions?: readonly string[];
  visible?: (record: Rec) => boolean;
  primary?: boolean;
  /** Returns true when the record must reload; a string navigates to that path (relative to Brocal). */
  run: (ctx: ActionContext, record: Rec) => Promise<boolean | string | void>;
}

export interface RecordConfig {
  key: string;
  icon: string;
  /** GET/PUT `{api}/{id}`, POST `{api}`. */
  api: string;
  /** List of the records (back link and destination after creating). */
  listPath: string;
  fields: FieldDef[];
  manage: readonly string[];
  /** The form can be edited (by status); default: always. */
  editable?: (record: Rec) => boolean;
  defaults?: (query: ParamMap, ctx: ActionContext) => Rec | Promise<Rec>;
  toForm?: (record: Rec) => Rec;
  /** Async completion before saving (e.g. prices from the price lists for empty lines). */
  prepare?: (value: Rec, ctx: ActionContext, record: Rec | null) => Promise<Rec>;
  toPayload?: (value: Rec, record: Rec | null) => unknown;
  create?: (api: BrocalApi, payload: unknown) => Observable<unknown>;
  title?: (record: Rec) => string;
  status?: { enum: string; value: (record: Rec) => string };
  subtitle?: (record: Rec, i18n: I18n) => string | null;
  actions?: RecordAction[];
  warnings?: (record: Rec) => string[];
}

/**
 * Page of a document with a life cycle (work order, workshop order, quote, trip, expense report, incident):
 * the form (editable while its status allows it) plus the actions of each status.
 */
@Component({
  selector: 'br-record-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, TPipe, EntityForm, PageHeader, StatusChip],
  template: `
    <div class="cl-page">
      <br-page-header [title]="title()" [icon]="config().icon" [subtitle]="subtitle()">
        <a mat-button [routerLink]="nav.to(config().listPath)"><mat-icon>arrow_back</mat-icon>{{ 'common.back' | t }}</a>
        @if (record(); as record) {
          @for (action of actions(); track action.key) {
            @if (action.primary) {
              <button mat-flat-button type="button" (click)="run(action)" [disabled]="busy()"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</button>
            } @else {
              <button mat-stroked-button type="button" (click)="run(action)" [disabled]="busy()"><mat-icon>{{ action.icon }}</mat-icon>{{ action.label | t }}</button>
            }
          }
        }
      </br-page-header>

      @if (busy()) {
        <mat-progress-bar mode="indeterminate" />
      }

      @if (record(); as record) {
        @if (config().status; as status) {
          <div class="br-actions"><span class="cl-muted">{{ 'common.status' | t }}:</span><br-status [enum]="status.enum" [value]="status.value(record)" /></div>
        }
        @if (warnings().length > 0) {
          <div class="br-warning-box">
            <mat-icon>warning</mat-icon>
            <ul>
              @for (warning of warnings(); track warning) {
                <li>{{ warning }}</li>
              }
            </ul>
          </div>
        }
      }

      @if (form(); as form) {
        <section class="br-card">
          <br-entity-form [fields]="config().fields" [form]="form" [editing]="!!record()" [record]="record()" />
          @if (canSave()) {
            <div class="br-actions br-form-actions">
              <span class="cl-spacer"></span>
              <button mat-flat-button type="button" (click)="save()" [disabled]="busy()"><mat-icon>save</mat-icon>{{ 'common.save' | t }}</button>
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .br-form-actions {
      margin-top: 16px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecordPage implements OnInit {
  private readonly api = inject(BrocalApi);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly dialogs = inject(Dialogs);
  private readonly i18n = inject(I18n);
  private readonly session = inject(BrocalSession);
  protected readonly nav = inject(BrocalNav);

  readonly id = input<string | null>(null);
  readonly recordConfig = input<RecordConfig | null>(null, { alias: 'record' });

  protected readonly config = computed(() => this.recordConfig() ?? (this.route.snapshot.data['record'] as RecordConfig));
  protected readonly record = signal<Rec | null>(null);
  protected readonly form = signal<FormGroup | null>(null);
  protected readonly busy = signal(false);

  protected readonly editable = computed(() => {
    const record = this.record();
    const config = this.config();
    return this.session.can(...config.manage) && (!record || !config.editable || config.editable(record));
  });
  protected readonly canSave = computed(() => this.editable());

  protected readonly title = computed(() => {
    const record = this.record();
    const config = this.config();
    return record ? (config.title?.(record) ?? this.i18n.t(`records.${config.key}`)) : this.i18n.t(`records.${config.key}New`);
  });

  protected readonly subtitle = computed(() => {
    const record = this.record();
    return record ? (this.config().subtitle?.(record, this.i18n) ?? null) : null;
  });

  protected readonly warnings = computed(() => {
    const record = this.record();
    return record ? (this.config().warnings?.(record) ?? []) : [];
  });

  protected readonly actions = computed(() => {
    const record = this.record();
    if (!record) {
      return [];
    }

    return (this.config().actions ?? []).filter((a) => (!a.permissions || this.session.can(...a.permissions)) && (!a.visible || a.visible(record)));
  });

  async ngOnInit(): Promise<void> {
    const id = this.id() ?? this.route.snapshot.paramMap.get('id');
    if (id) {
      await this.load(id);
    } else {
      const config = this.config();
      const defaults = (await config.defaults?.(this.route.snapshot.queryParamMap, actionContext(this.injector))) ?? {};
      this.setForm(defaults);
    }
  }

  private async load(id: string): Promise<void> {
    this.busy.set(true);
    try {
      const loaded = await firstValueFrom(this.api.get<Rec>(`${this.config().api}/${id}`));
      // Detail DTOs keep the id in their summary; actions read it from the record.
      const record = loaded['id'] ? loaded : { ...loaded, id };
      this.record.set(record);
      const config = this.config();
      this.setForm(config.toForm ? config.toForm(record) : record);
    } finally {
      this.busy.set(false);
    }
  }

  private setForm(value: Rec): void {
    const form = buildForm(this.config().fields, value);
    if (!this.editable()) {
      form.disable();
    }

    this.form.set(form);
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (!form) {
      return;
    }

    if (form.invalid) {
      form.markAllAsTouched();
      return;
    }

    const config = this.config();
    const record = this.record();
    this.busy.set(true);
    try {
      const read = readForm(config.fields, form);
      const value = config.prepare ? await config.prepare(read, actionContext(this.injector), record) : read;
      const payload = config.toPayload ? config.toPayload(value, record) : value;
      if (record) {
        await firstValueFrom(this.api.put(`${config.api}/${record['id'] as string}`, payload));
        this.dialogs.success(this.i18n.t('common.saved'));
        await this.load(record['id'] as string);
      } else {
        const created = await firstValueFrom(config.create ? config.create(this.api, payload) : this.api.post<{ id: string }>(config.api, payload));
        this.dialogs.success(this.i18n.t('common.saved'));
        const id = typeof created === 'string' ? created : (created as { id?: string } | null)?.id;
        await this.nav.navigate(id ? [config.listPath, id] : [config.listPath]);
      }
    } catch {
      // The error is shown by the HTTP error handler of the host.
    } finally {
      this.busy.set(false);
    }
  }

  protected async run(action: RecordAction): Promise<void> {
    const record = this.record();
    if (!record) {
      return;
    }

    this.busy.set(true);
    try {
      const result = await action.run(actionContext(this.injector), record);
      if (typeof result === 'string') {
        await this.nav.navigateByLink(result);
      } else if (result !== false) {
        await this.load(record['id'] as string);
      }
    } catch {
      // Shown by the HTTP error handler.
    } finally {
      this.busy.set(false);
    }
  }
}
