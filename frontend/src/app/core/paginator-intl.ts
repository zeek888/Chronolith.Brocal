import { Injectable, effect, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { I18n } from './i18n';

/** Paginator texts in the active language (Material only ships them in English). */
@Injectable()
export class TranslatedPaginatorIntl extends MatPaginatorIntl {
  private readonly i18n = inject(I18n);

  constructor() {
    super();
    effect(() => {
      this.itemsPerPageLabel = this.i18n.t('paginator.itemsPerPage');
      this.firstPageLabel = this.i18n.t('paginator.first');
      this.lastPageLabel = this.i18n.t('paginator.last');
      this.nextPageLabel = this.i18n.t('paginator.next');
      this.previousPageLabel = this.i18n.t('paginator.previous');
      this.changes.next();
    });
  }

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0 || pageSize === 0) {
      return this.i18n.t('paginator.rangeEmpty', { total: length });
    }

    const start = page * pageSize;
    return this.i18n.t('paginator.range', { start: start + 1, end: Math.min(start + pageSize, length), total: length });
  };
}
