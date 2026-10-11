import { ChangeDetectionStrategy, Component, TemplateRef, computed, contentChild, inject, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DatePipe, DateTimePipe, MoneyPipe, NumPipe } from '../core/format';
import { EnumPipe, I18n, TPipe } from '../core/i18n';
import { StatusChip } from './ui';

/** Text of a translation key, for computed cells (columns are declared as constants, before any language). */
export type Translate = (key: string, params?: Record<string, unknown>) => string;

export interface Column<T> {
  key: string;
  /** Translation key of the header. */
  label: string;
  type?: 'text' | 'money' | 'number' | 'date' | 'datetime' | 'status' | 'enum' | 'bool' | 'color';
  /** Enum name for `status` and `enum` columns. */
  enum?: string;
  value?: (row: T, t: Translate) => unknown;
  /** Secondary line under the value. */
  sub?: (row: T, t: Translate) => string | null | undefined;
  align?: 'end';
  /** Hidden on phones. */
  wide?: boolean;
  currency?: (row: T) => string;
}

/**
 * Lists of the module: columns by configuration, a link per row and server paging. Rows can be cards on
 * phones (`.br-table` collapses columns marked `wide`). Extra cell content goes in `<ng-template #actions>`.
 */
@Component({
  selector: 'br-table',
  imports: [NgTemplateOutlet, RouterLink, MatIconModule, MatPaginatorModule, MatProgressBarModule, MatTooltipModule, MoneyPipe, NumPipe, DatePipe, DateTimePipe, EnumPipe, TPipe, StatusChip],
  template: `
    <div class="cl-table-wrapper br-table-wrapper">
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <table class="br-table">
        <thead>
          <tr>
            @for (column of columns(); track column.key) {
              <th [class.br-end]="column.align === 'end'" [class.br-wide]="column.wide">{{ column.label | t }}</th>
            }
            @if (actions()) {
              <th class="cl-actions-cell"></th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track trackBy()(row)) {
            <tr [class.br-clickable]="!!link() || clickable()" [class.br-row-muted]="muted()?.(row)">
              @for (column of columns(); track column.key; let first = $first) {
                <td [class.br-end]="column.align === 'end'" [class.br-wide]="column.wide" [class.br-nowrap]="column.type === 'date' || column.type === 'datetime'">
                  @if (first && link(); as link) {
                    <a class="cl-row-link" [routerLink]="link(row)">
                      <ng-container [ngTemplateOutlet]="cell" [ngTemplateOutletContext]="{ $implicit: row, column }" />
                    </a>
                  } @else if (first && clickable()) {
                    <a class="cl-row-link" href="" (click)="$event.preventDefault(); activate.emit(row)">
                      <ng-container [ngTemplateOutlet]="cell" [ngTemplateOutletContext]="{ $implicit: row, column }" />
                    </a>
                  } @else {
                    <ng-container [ngTemplateOutlet]="cell" [ngTemplateOutletContext]="{ $implicit: row, column }" />
                  }
                  @if (subOf(row, column); as sub) {
                    <div class="br-sub">{{ sub }}</div>
                  }
                </td>
              }
              @if (actions(); as actions) {
                <td class="cl-actions-cell">
                  <ng-container [ngTemplateOutlet]="actions" [ngTemplateOutletContext]="{ $implicit: row }" />
                </td>
              }
            </tr>
          } @empty {
            @if (!loading()) {
              <tr>
                <td class="br-table-empty" [attr.colspan]="columns().length + (actions() ? 1 : 0)">{{ empty() ?? ('common.noRecords' | t) }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
    @if (paged()) {
      <mat-paginator [length]="total()" [pageIndex]="page() - 1" [pageSize]="pageSize()" [pageSizeOptions]="[10, 25, 50, 100]" (page)="changePage($event)" />
    }

    <ng-template #cell let-row let-column="column">
      @let value = valueOf(row, column);
      @switch (column.type) {
        @case ('money') {
          {{ $any(value) | money: (column.currency ? column.currency(row) : 'MXN') }}
        }
        @case ('number') {
          {{ $any(value) | num }}
        }
        @case ('date') {
          {{ $any(value) | d }}
        }
        @case ('datetime') {
          {{ $any(value) | dt }}
        }
        @case ('status') {
          <br-status [enum]="column.enum!" [value]="$any(value)" />
        }
        @case ('enum') {
          {{ $any(value) | enum: column.enum! }}
        }
        @case ('bool') {
          @if (value) {
            <mat-icon class="br-bool">check_circle</mat-icon>
          }
        }
        @case ('color') {
          <span class="br-swatch" [style.background]="value"></span>
        }
        @default {
          {{ value ?? '—' }}
        }
      }
    </ng-template>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DataTable<T> {
  readonly columns = input.required<Column<T>[]>();
  readonly rows = input.required<T[]>();
  readonly loading = input(false);
  readonly total = input(0);
  readonly page = input(1);
  readonly pageSize = input(25);
  readonly paged = input(true);
  readonly link = input<((row: T) => string[]) | null>(null);
  readonly muted = input<((row: T) => boolean) | null>(null);
  readonly empty = input<string | null>(null);
  readonly trackBy = input<(row: T) => unknown>((row: T) => (row as { id?: unknown }).id ?? row);

  /** First column opens the record through `activate` (edit dialogs) when there is no detail page. */
  readonly clickable = input(false);

  readonly pageChange = output<{ page: number; pageSize: number }>();
  readonly activate = output<T>();

  protected readonly actions = contentChild<TemplateRef<unknown>>('actions');
  protected readonly hasRows = computed(() => this.rows().length > 0);

  private readonly i18n = inject(I18n);
  private readonly translate: Translate = (key, params) => this.i18n.t(key, params);

  protected valueOf(row: T, column: Column<T>): unknown {
    return column.value ? column.value(row, this.translate) : (row as Record<string, unknown>)[column.key];
  }

  protected subOf(row: T, column: Column<T>): string | null | undefined {
    return column.sub?.(row, this.translate);
  }

  protected changePage(event: PageEvent): void {
    this.pageChange.emit({ page: event.pageIndex + 1, pageSize: event.pageSize });
  }
}
