# Brocal

**Documentación operativa para empresas de servicios petroleros**, multiempresa: boletas de servicio digitales
(captura en campo sin señal, firma del cliente, fotos y PDF con la marca de la empresa), órdenes de trabajo y
programación, tarifarios, flota y mantenimiento, permisos por unidad, combustible, inventario, expedientes del
personal (DC-3, H2S, SSPA), HSE, cuadrillas, viáticos, facturación CFDI 4.0, Carta Porte, cobranza con COPADE,
cotizaciones, dashboard gerencial, portal del cliente, reportes, bitácora, notificaciones (correo y WhatsApp),
integraciones (GPS, CONTPAQi/Aspel, Excel), flujos de autorización y reglas de negocio configurables por empresa
o para todas, y revisión automática de vencimientos.

Funciona **como módulo de ChronoLith** (se carga en el Shell bajo `/brocal` y toma su sesión, tema e idioma) o
**de forma independiente** con su propio Keycloak. Cada empresa tiene sus módulos contratados, cuotas, colores,
logotipos, configuración operativa y fiscal, usuarios y roles.

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, Clean Architecture, minimal APIs, EF Core 10 + PostgreSQL 17 |
| Frontend | Angular 22, Angular Material 3, Native Federation (remote del Shell y aplicación propia) |
| Seguridad | Keycloak 26 (OIDC, Authorization Code + PKCE), permisos por empresa y rol |

## Inicio rápido (independiente, Docker)

```bash
cp .env.standalone.example standalone.env       # una vez; ajusta contraseñas y SMTP si hace falta
docker compose -f docker-compose.standalone.yml --env-file standalone.env up -d --build
```

| Servicio | URL | Usuario de desarrollo |
|---|---|---|
| Brocal | http://localhost:4303 | `admin` / `Brocal2026!` (plataforma y empresa demo) |
| Usuarios de la empresa demo | — | `oficina`, `supervisor`, `operador`, `direccion`, `cliente` / `Demo2026!` |
| Consola de Keycloak | http://localhost:8181/admin | `admin` / `admin` |

Las credenciales son solo para desarrollo; cámbialas en `standalone.env` para otros ambientes. El modo
independiente lleva su propio archivo porque el `.env` de la carpeta es del modo módulo (apunta al Keycloak de
ChronoLith) y Compose lo leería por omisión; git ignora ambos.

## Licencia de ChronoLith (modo independiente)

En modo independiente la API valida una licencia de ChronoLith firmada (producto `brocal`); como módulo de
ChronoLith la valida el Shell y Brocal no registra nada de licencias. La licencia es de la instalación: todas las
empresas la comparten, junto con sus límites.

- **Sin una licencia válida la API no arranca**: registra un error crítico con el id de la instalación (para pedir
  la licencia) y termina. Vencida y fuera del periodo de gracia, queda en **solo lectura** (403 a los cambios),
  salvo iniciar sesión, marcar notificaciones como leídas, generar paquetes documentales y cargar una licencia nueva.
- **Configuración**: `License__Key` (token; en Compose, `LICENSE_KEY`) o `License__File` (ruta a un archivo con el
  token), y la licencia cargada desde la pantalla **Licencia**, que se guarda en la tabla `installation_license`.
  Manda la emitida más recientemente.
- **Desarrollo**: `appsettings.Development.json` y `docker-compose.standalone.yml` traen una licencia de desarrollo,
  que solo se acepta con `ASPNETCORE_ENVIRONMENT=Development` o con Keycloak en `localhost`.
- **Endpoints** (`api/license`): `GET status` para cualquier usuario con sesión (aviso en la barra); `GET` y `PUT`
  (cargar un token) solo para el administrador de la plataforma (rol `brocal-platform-admin`), no para el de una
  empresa.
- **Módulos**: la licencia concede módulos con productos `brocal.<módulo>` (cada uno incluye sus dependencias y los
  núcleo); una empresa solo usa los que contrató y la licencia incluye. Sin productos `brocal.*`, todos.
- **Límites** de toda la instalación: `users` (personal), `portalusers`, `companies`, `units`, `employees`, `tickets`
  (por mes), `storagemb` y `apikeys`; al alcanzarlos responde 403 con el tipo `licencia-limite`.

Detalle de la pantalla y los límites: [administración](docs/administracion.md#licencia-de-chronolith); configuración
en el servidor: [producción](docs/produccion.md#licencia).

## Como módulo de ChronoLith

Con `ChronoLith/infra` y `ChronoLith/shell` en ejecución:

```powershell
./scripts/ensure-keycloak-clients.ps1   # una vez: clientes chronolith-brocal-api / -web
docker compose up -d --build            # Brocal se registra en el Shell y aparece en el menú
```

Detalles, producción detrás del gateway y modo independiente: [docs/integracion-chronolith.md](docs/integracion-chronolith.md).

## Catálogos de ChronoLith (réplica)

Conectado a un Shell (como módulo, o independiente con `SHELL_API_URL` y una cuenta de servicio del realm
`chronolith`), Brocal guarda una **réplica local** de los catálogos maestros del Shell (pozos, instalaciones, campos,
compañías, cuencas, activos y equipos) y la mantiene al día con el **feed de cambios** del Shell cada
`Jobs:CatalogFeedMinutes` (2 min): la primera vez carga todo, después aplica solo los cambios y reporta su cursor. Las
locaciones (pozos e instalaciones) y los clientes pueden enlazarse a un elemento del maestro; al guardarlos, Brocal
envía al Shell sus datos compartidos con `X-ChronoLith-Source: brocal`, con el token del usuario o, si no puede, con
la cuenta de servicio desde un buzón de salida. *Publicar en ChronoLith* crea el registro en el maestro. El estado de
la réplica y los envíos se administra en **Administración de la plataforma → Catálogos de ChronoLith**. Sin Shell,
Brocal funciona con sus propios catálogos. Detalle: [integración](docs/integracion-chronolith.md#catálogos-compartidos-réplica-y-feed-de-cambios).

## Indicadores para la gerencia de ChronoLith

Con la misma conexión, Brocal publica en el Shell (fuente `brocal`) **11 indicadores por pozo o instalación del
catálogo compartido**, sumando todas las empresas cuyas locaciones están enlazadas a ese pozo o instalación: documentos
vigentes, por vencer, vencidos y faltantes, cumplimiento documental (%) y próximo vencimiento del personal y las unidades
asignados a órdenes abiertas; boletas del día y sin firmar; órdenes de trabajo abiertas; incidentes HSE del día y
abiertos. Se registran al arrancar, se recalculan desde un buzón de salida (`indicator_messages`) cuando cambia el dato
y se republican una vez al día. Lista, reglas de cálculo y envío: [integración](docs/integracion-chronolith.md#indicadores-para-la-gerencia).

## Desarrollo local

```powershell
./scripts/dev-up.ps1 -Start               # módulo: PostgreSQL + clientes de Keycloak + API y Angular
./scripts/dev-up.ps1 -Standalone -Start   # independiente: Keycloak propio + PostgreSQL + API y Angular
```

| Servicio | Puerto |
|---|---|
| Web (Angular, remote y página independiente) | 4303 |
| API (desarrollo; `/scalar` documenta la API) | 5303 |
| PostgreSQL | 5436 |
| Keycloak independiente | 8181 |

Pruebas y verificaciones:

```bash
cd backend  && dotnet test                     # dominio, aplicación (feed de catálogos e indicadores), arquitectura e integración (Testcontainers: requiere Docker)
cd frontend && npm run i18n:check && npx ng build
```

## Estructura

```
backend/    API .NET 10 (Domain, Application, Infrastructure, Api) y pruebas
frontend/   Angular 22: remote de Native Federation + aplicación independiente
infra/      realm "brocal" de Keycloak y tema de login de ChronoLith (modo independiente)
scripts/    clientes de Keycloak en el realm de ChronoLith y arranque de desarrollo
docs/       documentación
```

## Documentación

- [Arquitectura](docs/arquitectura.md): capas, multiempresa, seguridad, fiscal, tema y captura sin señal.
- [Módulos](docs/modulos.md): los 25 módulos, pantallas, permisos, roles predeterminados y API de integraciones.
- [Administración multiempresa](docs/administracion.md): alta de empresas, cuotas, marca, usuarios, roles, catálogos de
  ChronoLith y licencia.
- [Flujos, reglas y tareas programadas](docs/automatizacion.md): autorizaciones por pasos, reglas que bloquean o
  avisan (por empresa o globales) y revisión de vencimientos.
- [Integración con ChronoLith](docs/integracion-chronolith.md): contrato de módulo, desarrollo y producción.
- [Despliegue](docs/despliegue.md): imágenes, composiciones, configuración, respaldos y lista para producción.
- [Producción en brocal.chronolith.com.mx](docs/produccion.md): modo independiente en el servidor de ChronoLith con
  su Keycloak (realm `brocal`), paso a paso.

## Pendientes conocidos

- **PAC**: incluye el PAC "Simulado" (CFDI 4.0 completo con timbre de prueba, sin validez fiscal). Para timbrar en
  producción hay que implementar el adaptador del PAC contratado (`IPacProvider`).
- WhatsApp requiere una cuenta de WhatsApp Business (Cloud API de Meta) con plantillas aprobadas.
