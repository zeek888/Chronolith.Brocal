# Brocal

Documentación operativa multiempresa para empresas de servicios petroleros: boletas de servicio, órdenes de trabajo,
flota, personal, HSE, facturación CFDI, cobranza, flujos de autorización y reglas de negocio. Corre como módulo de
ChronoLith (remote de Native Federation en el Shell) o de forma independiente; en producción, independiente en
https://brocal.chronolith.com.mx con el Keycloak de ChronoLith (realm `brocal`).

Documentación en `docs/`: `arquitectura.md`, `modulos.md`, `administracion.md`, `automatizacion.md`,
`despliegue.md`, `produccion.md` e `integracion-chronolith.md`. Léela antes de cambiar un área.

## Estructura

- `backend/`: .NET 10 (`ChronoLith.Brocal.slnx`). `src/`: Domain, Application, Infrastructure y Api. `tests/`:
  Domain.UnitTests, Application.UnitTests (feed de catálogos e indicadores contra un Shell simulado), ArchitectureTests
  e IntegrationTests (Testcontainers; requieren Docker, salvo `SharedCatalogShellTests` e `IndicatorShellTests`). `nuget/`: paquetes de
  licenciamiento de ChronoLith (fuente local de `backend/nuget.config`; los `.nupkg` sí van en git).
- `frontend/`: Angular 22 sin zone.js, Material 3, Native Federation y Transloco (scope `brocal`).
- `deploy/`: producción; `deploy/update.sh` actualiza el servidor (git pull, respaldo si cambia la API, imágenes de
  una en una, comprobación y `--revertir`). `infra/keycloak/`: realm y tema del modo independiente local.
  `scripts/`: utilidades.

## Comandos

Desde `backend/`:
- Compilar: `dotnet build ChronoLith.Brocal.slnx`
- Pruebas: ejecuta los binarios directamente, porque `dotnet test` reporta "Zero tests ran". Por ejemplo
  `tests\ChronoLith.Brocal.Domain.UnitTests\bin\Debug\net10.0\ChronoLith.Brocal.Domain.UnitTests.exe`; igual con
  Application.UnitTests, ArchitectureTests e IntegrationTests (`-class <nombre completo>` filtra una clase).
- Migración: `dotnet ef migrations add <Nombre> --project src\ChronoLith.Brocal.Infrastructure --startup-project src\ChronoLith.Brocal.Api --output-dir Persistence\Migrations`

Desde `frontend/`:
- `npm run i18n:check`: cada llave usada debe existir en `src/app/i18n/es.json` y `en.json`.
- `npx ng build`: compilación estricta (`strictTemplates`).

Docker local:
- Independiente: `docker compose -f docker-compose.standalone.yml --env-file standalone.env up -d --build`
  (web 4303, Keycloak 8181, PostgreSQL 5436). Siempre con `--env-file`: el `.env` de la raíz es del modo módulo.
- Módulo: `docker compose up -d --build`, con `ChronoLith/infra` y `ChronoLith/shell` en ejecución.

## Backend

- Casos de uso CQRS en `Application/Features/<Área>`: comandos y consultas `sealed record`, manejadores
  `internal sealed`, validación con FluentValidation y decoradores de Scrutor (sin MediatR). Las pruebas de
  arquitectura lo verifican.
- Reglas de negocio con `Result` / `Error`, no con excepciones; los mensajes al usuario van en español.
- Minimal APIs en `Api/Endpoints`, todas bajo `/api` con política explícita (`RequirePermission`, `RequireTenant`,
  `RequirePlatformAdmin`); una prueba de integración falla si un endpoint no la declara.
- Multiempresa: las entidades de empresa implementan `ITenantOwned` (filtro global de EF); el trabajo de plataforma
  entra a cada empresa con `ITenantContext.BeginScope`.
- EF Core + PostgreSQL: esquema `brocal`, `snake_case`, concurrencia con `xmin` y enums como texto. Los eventos de
  dominio se despachan dentro de `SaveChanges` y sus manejadores no guardan.
- Todo cambio de esquema lleva una migración nueva; no edites una migración ya aplicada.
- Permisos en `Domain/Platform/Permissions.cs` y módulos en `Domain/Platform/BrocalModules.cs`, con nombres en
  español e inglés.
- Licencia de ChronoLith solo en modo independiente (`AddLicensing` de Infrastructure y `Program.cs`); en modo
  módulo no se registra nada. Una escritura que no es dato de negocio (sesión, notificaciones leídas, exportaciones
  por POST) lleva `.AllowWhenLicenseExpired()`. Las pruebas de integración firman sus licencias (`TestLicenses`).
  La licencia concede módulos (productos `brocal.<módulo>`; sin ellos, todos) que `TenantContext` aplica a cada
  empresa con `ILicensedModules`, y límites de toda la instalación (`ILicenseLimits.All`) que `QuotaGuard` revisa
  después de la cuota de la empresa. Un límite nuevo va en `ILicenseLimits`, en la pantalla Licencia y en
  `docs/administracion.md`.
- Catálogos del Shell: nunca se leen en vivo; se usa la réplica (`ISharedCatalogs`, tabla `shared_catalog_items`,
  global, sin filtro por empresa) que llena `CatalogFeedConsumer` desde el feed de cambios (reglas en
  `docs/integracion-chronolith.md`). Las escrituras al maestro pasan por `MasterCatalogSync` con
  `X-ChronoLith-Source: brocal` y, si fallan por permisos o red, por el buzón `catalog_push_messages`. Todo se
  activa solo con `Shell:ApiUrl` y la cuenta de servicio del realm `chronolith` (`Keycloak:ClientId` como módulo,
  `Shell:ClientId` independiente); sin ellos Brocal usa solo sus catálogos.
- Indicadores para el Shell (fuente `brocal`, contrato 7.4 de ChronoLith): definiciones y reglas en
  `Application/Features/Indicators` (`BrocalIndicators`, `IndicatorCalculator` puro, `IndicatorDataLoader`,
  `IndicatorPublisher`); el sujeto es el pozo o instalación del `ShellCatalogId` de la locación y suma todas las
  empresas. Un cambio que afecte un indicador debe quedar cubierto por `IndicatorChangeInterceptor` (marca en
  `indicator_messages` en la misma transacción); nunca llames al Shell de forma síncrona. Un indicador nuevo va en
  `BrocalIndicators.All` con textos es/en/pt-BR y en la tabla de `docs/integracion-chronolith.md`.

## Frontend

- Componentes standalone con signals y `ChangeDetectionStrategy.OnPush`; datos con `apiResource` y navegación con
  `BrocalNav`.
- Catálogos y documentos se declaran por configuración (`*.config.ts` sobre `ListPage` y `RecordPage`).
- Estilos globales `br-*` en `src/app/layout/brocal.scss`; las pantallas deben verse bien a 390 px de ancho.
- Ningún texto visible fijo en el código: usa una llave de Transloco en `es.json` y `en.json`.

## Reglas

- Interfaz, mensajes de error y documentación en español de México. Código C# y TypeScript (identificadores y
  comentarios) en inglés; los archivos de despliegue (compose, Nginx, scripts) se comentan en español.
- No leas ni subas `.env`, `standalone.env` ni `deploy/.env`: tienen credenciales reales (`.claude/settings.json`
  bloquea su lectura). Los `.env.example` solo llevan valores de ejemplo.
- Al terminar un cambio: compila, corre las pruebas que apliquen y `npm run i18n:check` si tocaste el frontend;
  actualiza `docs/` si cambió el comportamiento.
- Producción: sigue `docs/produccion.md` y no ejecutes nada en el servidor sin pedirlo.

Si un cambio altera comandos, estructura o convenciones descritas aquí, actualiza este archivo en el mismo cambio