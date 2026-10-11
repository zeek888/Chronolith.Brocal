import {
  withNativeFederation,
  fromPackageJson,
  shareAngularLocales,
} from '@angular-architects/native-federation/config';

/**
 * Brocal is a Native Federation remote of ChronoLith and, at the same time, a standalone application.
 * Inside the Shell it is mounted under /<routePath> from `./routes` and its dashboard widgets are
 * loaded from `./widgets/*`. Dependencies are shared as singletons with the same rules as the Shell,
 * so inside ChronoLith the module uses the Shell's HttpClient (Keycloak token), router, Transloco and theme.
 */
export default withNativeFederation({
  name: 'brocal',

  exposes: {
    './routes': './src/app/brocal.routes.ts',
    './widgets/resumen-mes': './src/app/widgets/month-summary.widget.ts',
    './widgets/vencimientos': './src/app/widgets/expiries.widget.ts',
    './widgets/flota': './src/app/widgets/fleet.widget.ts',
  },

  shared: {
    ...fromPackageJson({
      singleton: true,
      strictVersion: true,
      requiredVersion: 'auto',
      build: 'package',
    })
      // includeSecondaries is an opt-out of ignoreUnusedDeps, so all of
      // @angular/core is shared to prevent mismatches.
      .patch(['@angular/core'], { includeSecondaries: { keepAll: true } })
      .get(),
    // Locale data is not a secondary entry point: shared explicitly (same as the Shell).
    ...shareAngularLocales(['es-MX']),
  },

  // Bundled (with tree shaking) in the chunks of the module instead of being shared: charts, the
  // theme generator for company palettes and the Keycloak adapter of the standalone application.
  skip: [
    'rxjs/ajax',
    'rxjs/fetch',
    'rxjs/testing',
    'rxjs/webSocket',
    'echarts',
    'echarts/core',
    'echarts/charts',
    'echarts/components',
    'echarts/renderers',
    'keycloak-js',
    '@material/material-color-utilities',
    /^@angular\/(material|cdk)\/(.+\/)?(schematics|testing)(\/.*)?$/,
  ],

  features: {
    denseChunking: true,
  },
});
