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
        └────────────────┘       │              │──▶ Shell API (permisos, tema, manifiesto)
                                 │              │──▶ PAC, SMTP, WhatsApp Cloud API
                                 └──────────────┘
```

## Backend (`backend/`)

.NET 10 con Clean Architecture en cuatro proyectos (`ChronoLith.Brocal.slnx`):

| Proyecto | Contenido |
|---|---|
| `Domain` | Agregados y reglas de negocio por área: `Operations` (boletas, órdenes), `Commercial` (clientes, contratos, tarifarios, cotizaciones), `Fleet`, `Inventory`, `People`, `Compliance`, `Hse`, `Fiscal` (facturas, pagos, Carta Porte, estimaciones), `Automation` (catálogo de documentos y datos, flujos, solicitudes de autorización, reglas), `Platform` (empresas, membresías, roles, módulos, archivos, notificaciones, bitácora, ejecuciones programadas). Sin dependencias de infraestructura; setters privados y resultados `Result`. |
| `Application` | Casos de uso CQRS por funcionalidad (`Features/*`): comandos y consultas `sealed record` con su manejador `internal sealed`. Decoradores con Scrutor para validación (FluentValidation) y bitácora de ejecución; sin MediatR. |
| `Infrastructure` | EF Core 10 + Npgsql (esquema `brocal`, nombres `snake_case`, concurrencia con `xmin`), interceptores multiempresa y de auditoría, archivos en disco con URLs firmadas, PDF (PDFsharp/MigraDoc), Excel (ClosedXML), CFDI 4.0, correo (MailKit), WhatsApp, Keycloak, integración con el Shell y tareas programadas. |
| `Api` | *Minimal APIs* agrupadas por área (`Endpoints/*`), políticas de autorización, ProblemDetails en español, OpenAPI con Scalar, `/health`. |

Pruebas en `backend/tests`: dominio (xUnit v3), reglas de arquitectura (NetArchTest: dependencias entre capas,
manejadores `internal sealed`, comandos `sealed record`, sin setters públicos en el dominio) e integración
(WebApplicationFactory + PostgreSQL en Testcontainers): seguridad de todos los endpoints, aislamiento entre
empresas, ciclo completo boleta → firma → PDF → factura timbrada → pago, folios concurrentes, portal, API keys,
flujos de autorización (de empresa y globales), reglas que bloquean o avisan y tareas programadas.

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
| `core/` | Configuración (`API = new URL('api', import.meta.url)`), cliente HTTP con `X-Tenant`, sesión y permisos (`BrocalSession`, guardas `requires(...)`, directiva `*brCan`), navegación relativa a la base del módulo (`BrocalNav`), i18n (Transloco, *scope* `brocal`, español e inglés), tema, captura offline, formatos y fechas. |
| `shared/` | Componentes y motores genéricos: `CatalogPage`, `ListPage`, `RecordPage`, `EntityForm`/`EntityDialog` (formularios por configuración), tabla, selector de catálogos con búsqueda, firma, fotos, gráficas (ECharts, carga diferida). |
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
