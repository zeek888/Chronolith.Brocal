# Brocal

Documentación operativa multiempresa para empresas de servicios petroleros: boletas de servicio, órdenes de trabajo,
flota, personal, HSE, facturación CFDI, cobranza, flujos de autorización y reglas de negocio. Corre como módulo de
ChronoLith (remote de Native Federation en el Shell) o de forma independiente; en producción, independiente en
https://brocal.chronolith.com.mx con el Keycloak de ChronoLith (realm `brocal`).

Documentación en `docs/`: `arquitectura.md`, `modulos.md`, `administracion.md`, `automatizacion.md`,
`despliegue.md`, `produccion.md` e `integracion-chronolith.md`. Léela antes de cambiar un área.

## Estructura

- `backend/`: .NET 10 (`ChronoLith.Brocal.slnx`). `src/`: Domain, Application, Infrastructure y Api. `tests/`:
  Domain.UnitTests, ArchitectureTests e IntegrationTests (Testcontainers; requieren Docker).
- `frontend/`: Angular 22 sin zone.js, Material 3, Native Federation y Transloco (scope `brocal`).
- `deploy/`: producción; `deploy/update.sh` actualiza el servidor (git pull, respaldo si cambia la API, imágenes de
  una en una, comprobación y `--revertir`). `infra/keycloak/`: realm y tema del modo independiente local.
  `scripts/`: utilidades.

## Comandos

Desde `backend/`:
- Compilar: `dotnet build ChronoLith.Brocal.slnx`
- Pruebas: ejecuta los binarios directamente, porque `dotnet test` reporta "Zero tests ran". Por ejemplo
  `tests\ChronoLith.Brocal.Domain.UnitTests\bin\Debug\net10.0\ChronoLith.Brocal.Domain.UnitTests.exe`; igual con
  ArchitectureTests e IntegrationTests.
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