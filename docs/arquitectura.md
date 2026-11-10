# Arquitectura

Brocal es una aplicación multiempresa para la documentación operativa de empresas de servicios petroleros:
boletas de servicio, órdenes de trabajo, flota, personal, HSE, facturación CFDI y cobranza. Funciona de dos
formas con el mismo código:

- **Módulo de ChronoLith**: el Shell carga el frontend como *remote* de Native Federation bajo `/brocal`, provee
  la sesión de Keycloak (realm `chronolith`), el tema y el idioma; la API se registra sola en el Shell.
- **Independiente**: el mismo frontend arranca como aplicación propia con su Keycloak (realm `brocal`, tema de
  login de ChronoLith) y el tema configurado en ChronoLith o la marca de cada empresa.

```
┌──────────────── navegador ────────────────┐
│ Shell ChronoLith (host)   o   página propia│
│   └─ remote Brocal (Angular 22, ./routes)  │
└───────────────┬────────────────────────────┘
                │ JWT de Keycloak + X-Tenant
        ┌───────▼────────┐       ┌──────────────┐
        │ nginx (web)    │──/api─▶ API .NET 10  │──▶ PostgreSQL 17 (esquema brocal)
        │ remoteEntry,   │       │              │──▶ /data (archivos, llaves)
        │ config.json    │       │              │──▶ Keycloak (JWKS, alta de usuarios)
        └────────────────┘       │              │──▶ Shell API (permisos, tema, manifiesto,
                                 │              │      feed de cambios de catálogos y escritura al maestro)
                                 │              │──▶ PAC, SMTP, WhatsApp Cloud API
                                 └──────────────┘
```

## Backend (`backend/`)

.NET 10 con Clean Architecture en cuatro proyectos (`ChronoLith.Brocal.slnx`):

| Proyecto | Contenido |
|---|---|
| `Domain` | Agregados y reglas de negocio por área: `Operations` (boletas, órdenes), `Commercial` (clientes, contratos, tarifarios, cotizaciones), `Fleet`, `Inventory`, `People`, `Compliance`, `Hse`, `Fiscal` (facturas, pagos, Carta Porte, estimaciones), `Automation` (catálogo de documentos y datos, flujos, solicitudes de autorización, reglas), `Platform` (empresas, membresías, roles, módulos, archivos, notificaciones, bitácora, ejecuciones programadas, réplica de los catálogos de ChronoLith y buzón de envíos al maestro). Sin dependencias de infraestructura; setters privados y resultados `Result`. |
| `Application` | Casos de uso CQRS por funcionalidad (`Features/*`): comandos y consultas `sealed record` con su manejador `internal sealed`. Decoradores con Scrutor para validación (FluentValidation) y bitácora de ejecución; sin MediatR. `Features/SharedCatalogs` es el consumidor del feed de cambios y el envío al catálogo maestro. |
| `Infrastructure` | EF Core 10 + Npgsql (esquema `brocal`, nombres `snake_case`, concurrencia con `xmin`), interceptores multiempresa y de auditoría, archivos en disco con URLs firmadas, PDF (PDFsharp/MigraDoc), Excel (ClosedXML), CFDI 4.0, correo (MailKit), WhatsApp, Keycloak, integración con el Shell (permisos, tema, manifiesto, feed de catálogos y escritura al maestro) y tareas programadas. |
| `Api` | *Minimal APIs* agrupadas por área (`Endpoints/*`), políticas de autorización, ProblemDetails en español, OpenAPI con Scalar, `/health`. |

Pruebas en `backend/tests`: dominio (xUnit v3), aplicación (`Application.UnitTests`: el consumidor del feed de
catálogos contra un Shell simulado y los cuerpos que se envían al maestro), reglas de arquitectura (NetArchTest:
dependencias entre capas, manejadores `internal sealed`, comandos `sealed record`, sin setters públicos en el dominio)
e integración (WebApplicationFactory + PostgreSQL en Testcontainers): seguridad de todos los endpoints, aislamiento
entre empresas, ciclo completo boleta → firma → PDF → factura timbrada → pago, folios concurrentes, portal, API keys,
flujos de autorización (de empresa y globales), reglas que bloquean o avisan, tareas programadas, licencia (la API no
arranca sin ella, solo lectura con la licencia vencida y límites de usuarios y empresas; las pruebas firman sus
licencias con una llave propia) y el lado HTTP de los catálogos compartidos (token, encabezado `X-ChronoLith-Source`,
respuestas del maestro y análisis del feed, sin Docker).

### Multiempresa

- Cada solicitud indica la empresa en el encabezado **`X-Tenant`** (id o *slug*); los usuarios con una sola
  empresa no lo necesitan. `TenantResolutionMiddleware` valida la membresía, el estatus de la empresa y calcula
  los permisos efectivos (administradores: todos los de los módulos contratados; resto: unión de sus roles).
- Toda entidad de empresa implementa `ITenantOwned`: un **filtro global de EF** limita las consultas a la
  empresa de la solicitud y `TenantInterceptor` asigna la empresa a los registros nuevos y **bloquea cualquier
  escritura sobre registros de otra empresa**. Las operaciones de plataforma (alta de empresas, tareas
  programadas, inicio de sesión) trabajan en cada empresa con `ITenantContext.BeginScope`.
- Políticas: `tenant` (hay empresa), `platform-admin` y `tenant:<permiso1>|<permiso2>` (cualquiera de los
  permisos, con su módulo contratado). Una prueba de integración verifica que todo endpoint declare su política.

### Licencia de ChronoLith (modo independiente)

Solo con `Tenancy:Mode=Standalone` la API registra el licenciamiento (`ChronoLith.Licensing` y
`ChronoLith.Licensing.AspNetCore`, paquetes de `backend/nuget/`, producto `brocal`); como módulo lo valida el Shell
y no se registra nada.

- `Program.cs` carga la licencia **después de migrar** (`InitializeChronoLithLicenseAsync`): sin una válida, la API
  termina. El middleware de solo lectura va después de la autorización; las escrituras que no son datos de negocio
  se marcan con `AllowWhenLicenseExpired` (`POST me/session`, `POST me/notifications/read`,
  `POST compliance/package`).
- `Infrastructure/Licensing/LicenseStore` guarda el id de la instalación y la licencia cargada desde la pantalla en
  `installation_license`, una tabla de una sola fila **global** (no es `ITenantOwned`, sin filtro de empresa). Si
  dos réplicas la crean a la vez, gana la primera (misma llave primaria) y la otra relee.
- `Application/Features/Platform/LicenseLimits` cuenta los límites de la instalación (`users`, `portalusers`,
  `companies`, `units`, `employees`, `tickets`, `storagemb`, `apikeys`) en todas las empresas
  (`IgnoreQueryFilters`) y devuelve `LicenseLimitError`, que la API traduce a 403 con el tipo `licencia-limite`.
  `QuotaGuard` lo consulta después de la cuota de la empresa; los usuarios los revisan los manejadores de miembros y
  empresas, que conocen el correo. Recibe `ILicenseAccessor?` con valor por omisión `null`: en modo módulo, y antes
  de cargar la licencia (migración y empresa demo), no limita nada.
- `Application/Features/Platform/LicensedModules` (singleton) lee los productos `brocal.<módulo>` de la licencia
  activa, les agrega dependencias y núcleo, y lo recalcula cuando cambia `ILicenseAccessor.Version`. `TenantContext`
  lo aplica a cada `TenantSnapshot` que recibe (`Set` y `BeginScope`) y filtra los permisos, así que
  `IsModuleEnabled`, el menú, la autorización, los trabajos programados y las API keys ven los módulos efectivos sin
  cambiar lo guardado en la empresa. Alta de empresas y `PUT platform/tenants/{id}/modules` rechazan agregar módulos
  fuera de la licencia (`License.ModuleNotGranted`), y `GET platform/modules` marca cada módulo con `licensed`.
- Endpoints `api/license` (`MapChronoLithLicense`): `status` con cualquier sesión; detalle y carga con la política
  `platform-admin`.

### Bitácora y trazabilidad

`AuditTrailInterceptor` registra automáticamente cada alta, cambio (campos anteriores y nuevos) y baja de los
agregados; las acciones de negocio (firmar, cancelar, timbrar, exportar, reabrir) se registran explícitamente con
su motivo. Las boletas firmadas no se editan: se reabren con motivo, que queda en la bitácora.

### Folios, archivos y secretos

- Folios consecutivos por empresa y tipo de documento (`FolioSequence`), seguros ante capturas simultáneas.
- Archivos (fotos, firmas, PDF, XML, comprobantes) en `Files:RootPath`, hasta 25 MB; se sirven con **URLs
  firmadas con HMAC** (`Files:SigningKey`), con vencimiento y ligadas a la empresa.
- Contraseñas de PAC, SMTP y el token de WhatsApp se guardan cifrados con ASP.NET Data Protection (llaves en
  `DataProtection:KeysPath`).

### Notificaciones y tareas programadas

Los eventos de negocio (boleta firmada, orden asignada, unidad liberada de taller, incidente reportado, factura
timbrada…) generan avisos en la aplicación y mensajes en una bandeja de salida (*outbox*) que se envían por
correo o WhatsApp según las reglas de cada empresa. Cada hora (`Jobs:ScanIntervalMinutes`) `ScheduledJobsRunner`
revisa, empresa por empresa (`IDailyScanService` dentro de `BeginScope`), permisos, documentos, licencias,
contratos y tarifarios por vencer, documentos obligatorios faltantes, acciones correctivas, llaves de API,
mantenimiento, facturas vencidas, boletas sin facturar, existencias bajas, cotizaciones vencidas y autorizaciones
atrasadas (con llaves de deduplicación para no repetir avisos). Un *advisory lock* de PostgreSQL evita dos
revisiones a la vez y cada ejecución queda en `scheduled_job_runs` (la plataforma la consulta o la lanza desde
`plataforma/tareas`). Ver [tareas programadas](automatizacion.md#tareas-programadas).

### Catálogos compartidos de ChronoLith

Conectado a un Shell, Brocal mantiene una réplica **de la instalación** de los siete catálogos maestros
(`SharedCatalogItem`, tabla `shared_catalog_items`: id del Shell como llave, clave, nombre, padre, activo, `updatedAt`
del maestro y la foto JSON del DTO; no es `ITenantOwned`). `CatalogFeedConsumer` (Application) sigue el feed de cambios
del Shell con la cuenta de servicio (`ICatalogFeedClient`): aplica cada página en orden sobre `ICatalogReplica`,
adopta el cursor de la respuesta y lo guarda (`catalog_feed_state`, una fila) después de aplicar la página, reporta los
ids aplicados y los que fallaron, aplica las reproducciones, hace la carga completa con `requiresFullSync` o la primera
vez, y reconoce los cambios propios por `source: brocal`. `CatalogSyncService` lo ejecuta cada `Jobs:CatalogFeedMinutes`
con un *advisory lock*; el administrador de la plataforma lo lanza desde *Catálogos de ChronoLith*.

En sentido contrario, `MasterCatalogSync` envía al maestro los cambios de locaciones y clientes enlazados
(`ICatalogSpec.AfterCommitAsync` de `SiteSpec` y `CustomerSpec`, y *Publicar en ChronoLith*): primero con el token del
usuario (`IMasterCatalogWriter`, siempre con `X-ChronoLith-Source: brocal`) y, si el Shell lo rechaza por permisos o
no responde, por el buzón `catalog_push_messages`, que la cuenta de servicio vacía con reintentos exponenciales al
inicio de cada sincronización. `LinkedRecordsReconciler` resuelve los conflictos al aplicar el feed: gana el lado que
cambió más tarde (`updatedAt`), los envíos pendientes superados se descartan y las locaciones de un elemento
desactivado se desactivan. Sin `Shell:ApiUrl` o sin cuenta de servicio nada de esto se activa y los selectores de
ChronoLith no aparecen.

### Indicadores para el Shell

Con la misma conexión, Brocal publica indicadores por pozo o instalación del catálogo compartido (fuente `brocal`, ver
[integración](integracion-chronolith.md#indicadores-para-la-gerencia)). `IndicatorChangeInterceptor` (interceptor de EF)
agrega en el mismo `SaveChanges` del cambio de negocio una marca `IndicatorMessage` (tabla global `indicator_messages`)
con las locaciones, sujetos y días afectados. `IndicatorPublishingService` registra las definiciones, encola la
republicación diaria y llama a `IndicatorPublisher` (Application): junta las marcas, lee los datos de todas las empresas
con `IndicatorDataLoader` (`IgnoreQueryFilters`, suma las locaciones enlazadas a cada sujeto), calcula los valores con
`IndicatorCalculator` (puro, probado sin base de datos), guarda las solicitudes de hasta 500 valores y los retiros, y
las entrega en orden con `IIndicatorClient` (cuenta de servicio); un 400 se descarta.

### Flujos de autorización y reglas de negocio

`AutomationCatalog` (dominio) declara, por documento, los momentos de su ciclo de vida y los **datos** que pueden
usar las condiciones; `DocumentFacts` los calcula al momento de evaluar. Las definiciones (`WorkflowDefinition`,
`BusinessRule`) tienen `TenantId` opcional: sin empresa son **globales**, fuera del filtro multiempresa, y cada
empresa puede desactivar las opcionales (`AutomationOptOut`).

- `IBusinessRules.EnforceAsync` se llama en cada comando que cambia un documento en un momento con reglas. Un
  bloqueo regresa `422 Rules.Blocked`; su aviso se guarda en un *scope* aparte (`IIsolatedWriter`) porque la
  operación se revierte. Las reglas que avisan se suman a la misma unidad de trabajo.
- `IWorkflowEngine` elige el primer flujo que aplica (globales obligatorios, de la empresa, globales opcionales) y
  crea una `WorkflowInstance` con los pasos copiados de la definición. En boletas y viáticos (`Execute`) la última
  aprobación hace la transición del documento; en órdenes, cotizaciones, facturas y órdenes de taller (`Unlock`)
  la acción se detiene con `ApprovalPendingError`, que la API convierte en **`202 Accepted`**
  (`{ approvalPending, instanceId, message }`), y procede cuando hay una aprobación vigente.
- Una aprobación vale mientras no cambien los datos del documento: la solicitud guarda una huella SHA-256 de los
  datos no volátiles (`FactsHash`).
- Los eventos de dominio (`WorkflowRequestedEvent`, `WorkflowStepAdvancedEvent`, `WorkflowCompletedEvent`) avisan a
  los aprobadores y a quien solicitó, y aplican la decisión al documento dentro del mismo `SaveChanges`.
- En el frontend, `BrocalApi` pasa cada respuesta `202` a `ApprovalNotices` (aviso "en espera de autorización") y
  `br-workflow-panel` muestra el estado en cada documento.

### Fiscal

Generador propio de CFDI 4.0 (ingreso, traslado), complemento de Pagos 2.0 y Carta Porte 3.1. El timbrado pasa
por `IPacProvider` (`Infrastructure/Fiscal/PacGateway.cs`): hoy existe el PAC **"Simulado"** (XML real con un
timbre de prueba, sin validez fiscal). Para producción se implementa el adaptador del PAC contratado (sellado y
timbrado con los CSD de la empresa registrados en el PAC) y se registra junto al simulado.

## Frontend (`frontend/`)

Angular 22 (componentes *standalone*, *signals*, sin zone.js), Angular Material 3 y Native Federation:

| Carpeta | Contenido |
|---|---|
| `core/` | Configuración (`API = new URL('api', import.meta.url)`), cliente HTTP con `X-Tenant`, sesión y permisos (`BrocalSession`, guardas `requires(...)`, directiva `*brCan`, `sharedCatalogsEnabled()` para los campos de ChronoLith), navegación relativa a la base del módulo (`BrocalNav`), i18n (Transloco, *scope* `brocal`, español e inglés), tema, captura offline, formatos y fechas. |
| `shared/` | Componentes y motores genéricos: `CatalogPage` (con acciones de renglón por configuración, `rowActions`), `ListPage`, `RecordPage`, `EntityForm`/`EntityDialog` (formularios por configuración), tabla, selector de catálogos con búsqueda (también sobre la réplica de ChronoLith, `catalogs/shell/<catálogo>`), firma, fotos, gráficas (ECharts, carga diferida). |
| `features/` | Pantallas por área, mayormente declaradas como configuración (`*.config.ts`) sobre los motores genéricos. |
| `layout/` | Banda superior dentro del Shell o barra lateral en modo independiente; estilos globales `br-*`. |
| `widgets/` | Widgets para el tablero del Shell (`resumen-mes`, `vencimientos`, `flota`). |
| `standalone/` | Arranque independiente: Keycloak (PKCE), interceptores y configuración de ejecución (`config.json`). |

`federation.config.mjs` expone `./routes` y los tres widgets; comparte Angular, Material y Transloco como
*singleton* con el Shell, y empaqueta ECharts, keycloak-js y material-color-utilities dentro del módulo.

### Tema

Cada empresa elige **usar el estilo de ChronoLith** o una **paleta propia** de 8 colores semilla (primario,
secundario, acento, neutro, error, advertencia, éxito, información) más esquinas, tipografía, densidad, modo de
color y logotipos. `theme-generator.ts` deriva las variables `--mat-sys-*` y `--cl-*` con
`material-color-utilities`. Dentro del Shell el tema propio se aplica solo al contenedor `.brocal-themed` (y a
los diálogos de Brocal); fuera, a la página completa. Sin marca propia, Brocal hereda el tema del Shell (módulo)
o el tema predeterminado de ChronoLith leído de su API (independiente).

### Captura en campo sin señal

Las boletas se capturan aunque no haya conexión: la captura (con fotos comprimidas y firma) se guarda en
IndexedDB (`brocal-offline`) con un `clientId` y se envía al recuperar la señal (alta idempotente por
`clientId`, luego fotos y firma). Los catálogos usados en la captura se guardan en caché local.

### Calidad

`npm run i18n:check` verifica que cada llave usada en el código exista en español e inglés (se ejecuta también
en el build de Docker). `npx ng build` compila en modo estricto (`strict`, `strictTemplates`).
