import { Dialog } from '@angular/cdk/dialog';
import { registerLocaleData } from '@angular/common';
import localeEsMx from '@angular/common/locales/es-MX';
import { inject } from '@angular/core';
import { DateAdapter, MAT_DATE_FORMATS } from '@angular/material/core';
import { MatDatepickerIntl } from '@angular/material/datepicker';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { Routes } from '@angular/router';
import { ISO_DATE_FORMATS, IsoDateAdapter, TranslatedDatepickerIntl } from './core/dates';
import { I18n } from './core/i18n';
import { BROCAL_ROOT } from './core/nav';
import { TranslatedPaginatorIntl } from './core/paginator-intl';
import { sessionReady } from './core/session';
import { FEATURE_ROUTES } from './features/feature.routes';
import { BrocalLayout } from './layout/brocal-layout';

registerLocaleData(localeEsMx);

/**
 * Routes exposed to the ChronoLith Shell (`./routes`), mounted under the route registered in the manifest
 * (`/brocal`). The Shell provides HttpClient with the Keycloak token, Transloco, icons and the theme; only
 * the providers of the module go here. The standalone application mounts the same routes at its root.
 */
export const routes: Routes = [
  {
    path: '',
    component: BrocalLayout,
    data: { [BROCAL_ROOT]: true },
    canActivate: [() => inject(I18n).ready(), sessionReady],
    providers: [
      { provide: DateAdapter, useClass: IsoDateAdapter },
      { provide: MAT_DATE_FORMATS, useValue: ISO_DATE_FORMATS },
      { provide: MatDatepickerIntl, useClass: TranslatedDatepickerIntl },
      { provide: MatPaginatorIntl, useClass: TranslatedPaginatorIntl },
      // Own instances (MatDialog uses the CDK Dialog) so dialogs are created with this route's injector
      // and see its providers (date adapter, calendar texts).
      Dialog,
      MatDialog,
    ],
    children: FEATURE_ROUTES,
  },
];

export default routes;
