import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NgControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { catchError, of, switchMap } from 'rxjs';
import { Query } from '../core/api';
import { TPipe } from '../core/i18n';
import { Lookups } from '../core/lookups';
import { Lookup } from '../core/models';

/**
 * Selector over a catalog lookup (`catalogs/customers/lookup`…) with search, single or multiple. It binds
 * the form control of its host directly to the inner mat-select, so validation and errors show as usual:
 * `<br-lookup formControlName="customerId" catalog="catalogs/customers" [label]="..." />`.
 */
@Component({
  selector: 'br-lookup',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatSelectModule, MatIconModule, TPipe],
  template: `
    <mat-form-field class="br-full" [subscriptSizing]="subscript()">
      <mat-label>{{ label() }}</mat-label>
      <mat-select [formControl]="control" [multiple]="multiple()" (openedChange)="opened($event)" (selectionChange)="emit()" [panelWidth]="null">
        @if (searchable()) {
          <div class="br-lookup__search">
            <mat-icon>search</mat-icon>
            <input #search [placeholder]="'common.search' | t" (input)="term.set(search.value)" (keydown)="$event.stopPropagation()" />
          </div>
        }
        @if (!multiple() && !required()) {
          <mat-option [value]="null">—</mat-option>
        }
        @for (item of visible(); track item.id) {
          <mat-option [value]="item.id" [class.br-inactive]="!item.isActive">
            @if (showCode() && item.code !== item.name) {
              <span class="br-code">{{ item.code }}</span>
            }
            {{ item.name }}
            @if (showExtra() && item.extra) {
              <span class="cl-muted"> · {{ item.extra }}</span>
            }
          </mat-option>
        }
        @if (visible().length === 0) {
          <mat-option disabled>{{ 'common.noResults' | t }}</mat-option>
        }
      </mat-select>
      @if (hint(); as hint) {
        <mat-hint>{{ hint }}</mat-hint>
      }
      <mat-error>{{ 'validation.required' | t }}</mat-error>
    </mat-form-field>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LookupSelect implements ControlValueAccessor {
  private readonly lookups = inject(Lookups);
  private readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly fallback = new FormControl<unknown>(null);

  readonly catalog = input<string | null>(null);
  readonly query = input<Query | null>(null);
  /** Static options instead of a catalog. */
  readonly items = input<Lookup[] | null>(null);
  readonly label = input.required<string>();
  readonly multiple = input(false);
  readonly required = input(false);
  readonly showCode = input(true);
  readonly showExtra = input(true);
  readonly includeInactive = input(false);
  readonly hint = input<string | null>(null);
  readonly subscript = input<'fixed' | 'dynamic'>('dynamic');
  /** Restricts the options (e.g. sites of the selected customer, keeping the general ones). */
  readonly filter = input<((item: Lookup) => boolean) | null>(null);

  /** Initial value when the selector is used without a form control (list filters). */
  readonly value = input<string | null>(null);

  readonly selected = output<Lookup | Lookup[] | null>();

  protected readonly term = signal('');

  private readonly loaded = toSignal(
    toObservable(computed(() => ({ catalog: this.catalog(), query: this.query(), inactive: this.includeInactive() }))).pipe(
      switchMap(({ catalog, query, inactive }) =>
        catalog ? this.lookups.list(catalog, { ...(query ?? {}), activeOnly: !inactive }).pipe(catchError(() => of([] as Lookup[]))) : of([] as Lookup[]),
      ),
    ),
    { initialValue: [] as Lookup[] },
  );

  readonly options = computed(() => {
    const items = this.items() ?? this.loaded();
    const filter = this.filter();
    return filter ? items.filter(filter) : items;
  });

  protected readonly searchable = computed(() => this.options().length > 8);

  protected readonly visible = computed(() => {
    const term = this.term().trim().toLowerCase();
    const options = this.options();
    if (!term) {
      return options;
    }

    const selected = new Set(([] as unknown[]).concat(this.control.value ?? []));
    return options.filter((o) => selected.has(o.id) || o.name.toLowerCase().includes(term) || o.code.toLowerCase().includes(term) || (o.extra ?? '').toLowerCase().includes(term));
  });

  constructor() {
    if (this.ngControl) {
      this.ngControl.valueAccessor = this;
    } else {
      effect(() => this.fallback.setValue(this.value()));
    }

    // Emits the selected item once the options arrive (forms opened with a value).
    effect(() => {
      if (this.options().length > 0 && this.control.value) {
        this.emit();
      }
    });
  }

  get control(): FormControl {
    return (this.ngControl?.control as FormControl | null) ?? (this.fallback as FormControl);
  }

  find(id: string | null | undefined): Lookup | undefined {
    return id ? this.options().find((o) => o.id === id) : undefined;
  }

  protected opened(open: boolean): void {
    if (!open) {
      this.term.set('');
    }
  }

  protected emit(): void {
    const value = this.control.value as string | string[] | null;
    if (Array.isArray(value)) {
      this.selected.emit(this.options().filter((o) => value.includes(o.id)));
    } else {
      this.selected.emit(this.find(value) ?? null);
    }
  }

  // The host control is bound straight to the inner mat-select: nothing to do here.
  writeValue(): void {}
  registerOnChange(): void {}
  registerOnTouched(): void {}
}
