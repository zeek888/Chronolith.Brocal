# Integración con ChronoLith

Brocal sigue el contrato de módulos del Shell (`ChronoLith/docs/module-contract.md`), igual que Actividades y
Aforo: se publica por separado y se registra solo. **No requiere cambios en el repositorio de
ChronoLith**, salvo la entrada del gateway en producción.

## Contrato

| Elemento | Brocal |
|---|---|
| Remote de Native Federation | `remoteEntry.json` en la URL pública del módulo; expone `./routes` y los widgets `./widgets/resumen-mes`, `./widgets/vencimientos` y `./widgets/flota` (entradas `settings` y `moduleRoute`). |
| Compartidos *singleton* | Angular 22, Angular Material y CDK, Transloco 8.4 y RxJS (mismas versiones que el Shell). |
| API | `new URL('api', import.meta.url)`: el mismo origen del remote; el Shell la conoce como `apiBaseUrl` y agrega el token. El frontend agrega `X-Tenant` con la empresa seleccionada. |
| Manifiesto | `PUT {shell-api}/api/modules/manifest` al arrancar, con token de `chronolith-brocal-api` (rol `chronolith-module-registrar`, audiencia `chronolith-api`); se reintenta con espera creciente hasta lograrlo. |
| Permisos del Shell | `brocal.access` (abrir Brocal) y `brocal.platform.manage` (administrar empresas), consultados en `GET {shell-api}/api/me/permissions?module=brocal` con el token del usuario (caché de `Shell:PermissionsCacheSeconds`, 120 s). Lo demás se decide por empresa con los roles de Brocal. |
| Tokens | Realm `chronolith`, audiencia `chronolith-api`; administradores de la plataforma por rol de realm `chronolith-admin` o `brocal-platform-admin`. |
| Apariencia | Sin marca propia, Brocal usa el tema del Shell (`--mat-sys-*`, `--cl-*`, modo oscuro `cl-dark`). Con marca propia, la empresa aplica su paleta solo al área de Brocal. |
| Idioma | *Scope* `brocal` de Transloco: sigue el idioma del Shell y envía `Accept-Language` a la API. |
| Catálogos compartidos | **Réplica local** de los siete catálogos maestros del Shell alimentada por su feed de cambios (`GET /api/catalogs/changes?consumer=brocal`); los selectores leen la réplica (`GET /api/catalogs/shell/{catálogo}/lookup`) y las locaciones y clientes enlazados se envían al maestro con `X-ChronoLith-Source: brocal`. Ver [Catálogos compartidos](#catálogos-compartidos-réplica-y-feed-de-cambios). |
| Indicadores | Fuente `brocal` de la sección 7.4 del contrato (`ChronoLith/docs/indicators.md`): 11 indicadores por pozo o instalación del catálogo compartido, publicados desde el buzón `indicator_messages` con la cuenta de servicio. Ver [Indicadores para la gerencia](#indicadores-para-la-gerencia). |
| Errores | ProblemDetails con `detail` en español; los muestra el interceptor de errores del Shell. |
| Licencia | La valida el Shell con la licencia de la plataforma (producto `brocal`): sin ella no da permisos de Brocal y, vencida, solo los de lectura. Como módulo, Brocal no registra nada de licencias ni muestra la pantalla Licencia. |

El manifiesto declara el menú del Shell (Inicio, Boletas de servicio, Órdenes de trabajo, Dashboard gerencial y,
para administradores, Administración de la plataforma), los dos permisos y los tres widgets, en español e inglés.
Dentro de Brocal, una banda superior muestra el selector de empresa y el menú completo según los permisos.

## Catálogos compartidos: réplica y feed de cambios

El Shell es dueño de los datos maestros (cuencas, activos, compañías, campos, instalaciones, equipos y pozos). Brocal
no los consulta en vivo: guarda una **réplica de la instalación** (tabla `shared_catalog_items`, fuera del filtro por
empresa: todas las empresas ven los mismos pozos) y la mantiene al día con el **feed de cambios** del Shell, siguiendo
la sección 5.1 de `ChronoLith/docs/module-contract.md`:

| Regla del contrato | Cómo lo hace Brocal |
|---|---|
| Consumidor = código del producto | `consumer=brocal`, con la cuenta de servicio del realm `chronolith` (rol `chronolith-module-registrar`): la del módulo (`Keycloak:ClientId`) o, en modo independiente, `Shell:ClientId` / `Shell:ClientSecret` / `Shell:AuthServerUrl`. |
| Consulta cada 1 a 5 minutos, repite mientras `hasMore` | `CatalogSyncService` cada `Jobs:CatalogFeedMinutes` (2 por omisión), hasta 25 páginas de 200 por ejecución; un *advisory lock* de PostgreSQL impide dos consumidores a la vez. |
| Aplicar en orden e idempotente; `deleted` desactiva | *Upsert* por id del Shell; `deleted` deja el elemento inactivo. Un cambio más viejo que la foto guardada (`updatedAt`) no la sobrescribe. |
| `replay: true` se aplica aunque esté detrás del cursor | Las reproducciones van primero y siempre se aplican. |
| Adoptar el `cursor` de la respuesta y guardarlo después de aplicar la página | `catalog_feed_state` (una sola fila) se guarda junto con la página; si un administrador regresó el cursor, Brocal lo adopta. |
| `requiresFullSync` → carga completa | Descarga los siete catálogos paginados desde `/api/catalogs/{catálogo}` y continúa desde el `cursor` recibido. La primera vez (cursor 0 y réplica vacía) hace lo mismo; *Recargar completo* en la pantalla también. |
| Reportar después de cada página | `PUT /api/catalogs/changes/consumers/brocal` con el cursor, los ids aplicados (reproducciones incluidas) y los que fallaron con su mensaje. |
| Un cambio que falla no detiene el feed | Se reporta como error; el resto de la página se aplica y el cursor avanza. |
| Cambios propios | Toda escritura al maestro lleva `X-ChronoLith-Source: brocal`; cuando vuelve por el feed, actualiza la réplica pero no toca de nuevo la locación o el cliente. |

**Enlace y envío al maestro.** Una locación de tipo pozo o instalación puede enlazarse a un pozo o instalación del
Shell (`shellCatalogId`, con su campo `shellFieldId`) y un cliente a una compañía (`shellCompanyId`). Al crear o
editar un registro enlazado, Brocal envía al maestro los campos compartidos (clave, nombre, coordenadas y campo de la
locación; clave, nombre y RFC del cliente) con `PUT /api/catalogs/{wells|facilities|companies}/{id}`, conservando lo
que el maestro sabe y Brocal no (tipo, trayectoria, estatus, equipo, fechas, notas). *Publicar en ChronoLith* (menú
del renglón) crea el registro en el maestro con `POST` o lo enlaza si su clave ya existe allí; un pozo nuevo requiere
el campo de ChronoLith. El envío va primero con el token del usuario; si no tiene `shell.catalogs.manage` o el Shell
no responde, queda en el buzón `catalog_push_messages` y lo envía la **cuenta de servicio** con reintentos (1, 2, 4…
minutos, hasta 8). Para eso la cuenta de servicio necesita, además del rol de realm `chronolith-module-registrar`, un
rol del Shell con el permiso `shell.catalogs.manage`: en *Administración → Usuarios* del Shell, asigna ese rol al
usuario `service-account-chronolith-brocal-api` (o al de `Shell:ClientId`). Lo que el maestro rechaza (400, 404, 409)
queda como *rechazado* en la pantalla hasta corregir el registro.

**Conflictos.** Gana el lado que cambió más tarde (`updatedAt`): si el maestro cambió después que la locación o el
cliente, al llegar por el feed se aplican sus campos compartidos (la clave solo si no choca en la empresa) y se
descartan los envíos pendientes de ese registro; si el registro local es más nuevo, se encola su envío. Un pozo o
instalación desactivado en el maestro desactiva las locaciones enlazadas.

**Pantalla.** *Administración de la plataforma → Catálogos de ChronoLith* (`plataforma/catalogos-chronolith`, rol
`brocal-platform-admin` o permiso `brocal.platform.manage`): cursor y último cambio del Shell, última consulta,
reporte y carga completa, conteos por catálogo, último error, envíos pendientes, rechazados o agotados (reintentar o
descartar) y los botones **Consultar ahora** y **Recargar completo**. Sin `Shell:ApiUrl` y cuenta de servicio, la
pantalla avisa que Brocal no está conectado y los formularios no muestran los campos de ChronoLith.

## Indicadores para la gerencia

Brocal publica en el Shell **el resultado** de sus datos por pozo o instalación (sección 7.4 de
`ChronoLith/docs/module-contract.md`, especificación completa en `ChronoLith/docs/indicators.md`), para que la gerencia
los vea en tableros, gráficas, reportes y alertas del Shell sin entrar a Brocal. Se activa con la misma conexión que los
catálogos compartidos (`Shell:ApiUrl` y la cuenta de servicio del realm `chronolith`, como módulo o independiente) y se
puede apagar con `Indicators:Enabled=false`.

**Sujeto.** El pozo (`well`) o la instalación (`facility`) del catálogo compartido al que está enlazada una locación
(`shellCatalogId`); el tipo sale de la réplica (`wells` o `facilities`). Brocal es multiempresa: el valor de un pozo
**suma las locaciones de todas las empresas operativas** enlazadas a él (activas o en una prueba vigente; las
suspendidas o canceladas conservan sus registros pero ya no cuentan). Las locaciones sin enlace (patios, campos,
locaciones propias) no publican nada, y un enlace a un elemento que la réplica todavía no conoce se omite (el Shell
rechazaría la solicitud completa).

Un sujeto **se publica** mientras lo enlaza al menos una locación activa de una empresa operativa y el elemento está
activo en la réplica. Si deja de cumplirse (se desvinculó o se dio de baja la última locación, se suspendió o canceló la
última empresa, o el pozo se desactivó o se borró en el Shell), sus valores **se retiran** del Shell. La tabla
`indicator_subjects` recuerda qué sujetos se publicaron y cuáles ya se retiraron: así un retiro no se repite cada día y la
republicación diaria retira también los sujetos publicados antes que ya no se pueden publicar.

| Clave (`brocal.…`) | Tipo | Entre sujetos | Entre días | Cómo se calcula |
|---|---|---|---|---|
| `documents.valid` | number | sum | snapshot | Documentos requeridos del personal y las unidades **desplegados** (ver abajo) vigentes fuera de la ventana de aviso o sin vencimiento. |
| `documents.expiring` | number | sum | snapshot | Los que vencen dentro de la ventana de aviso (la del tipo de documento o `ExpiryWarningDays` de la empresa). `severity: warning` si hay alguno. |
| `documents.expired` | number | sum | snapshot | Los ya vencidos. `severity: error` si hay alguno. |
| `documents.missing` | number | sum | snapshot | Los requeridos que no se han capturado. `severity: warning` si hay alguno. |
| `documents.compliance` | percent (1 decimal) | avg | snapshot | (vigentes + por vencer) / requeridos × 100; `severity` error con vencidos, warning con por vencer o faltantes, success al 100 %. La nota dice cuántas personas, unidades y órdenes cuenta. Si no hay nada requerido (nadie desplegado) se publica **100 sin `severity`** con la nota «Sin personal ni unidades desplegados», para que el último porcentaje no se quede como actual al cerrar las órdenes. |
| `documents.next-expiry` | date | none | snapshot | Vencimiento más próximo de un documento vigente o por vencer; la nota dice tipo y clave de la persona o unidad. `warning` si ya está en la ventana de aviso. Si ya no hay ninguno (sin órdenes abiertas, o todo vencido o faltante) se **retira** con `DELETE` (una fecha no puede publicarse vacía), una sola vez. |
| `tickets.services` | number | sum | daily | Boletas de servicio con fecha de servicio de ese día, sin las canceladas. |
| `tickets.unsigned` | number | sum | snapshot | Boletas en captura (borrador) de cualquier fecha. |
| `work-orders.open` | number | sum | snapshot | Órdenes programadas, despachadas o en proceso. |
| `hse.incidents` | number | sum | daily | Incidentes, accidentes, casi accidentes y condiciones inseguras ocurridos ese día (fecha en la zona de la empresa). |
| `hse.open-incidents` | number | sum | snapshot | Incidentes reportados o en investigación. `severity: error` si alguno es de severidad alta o crítica, `warning` si no. |

- **Personal y unidades desplegados**: las unidades y el personal (incluidos los integrantes de la cuadrilla) de las
  órdenes de trabajo abiertas en las locaciones del sujeto, solo los activos. **Documentos requeridos**: los tipos
  obligatorios activos de su empresa más los que pide el requisito documental del contrato de la orden (o del cliente,
  si el requisito no tiene contrato); por cada persona o unidad y tipo cuenta el documento más reciente, con el mismo
  semáforo que la matriz de cumplimiento de Brocal.
- Ningún indicador lleva `quantity` (son conteos, porcentajes y fechas) ni `planned` (Brocal no tiene un programa contra
  el cual comparar). Las definiciones van en español con traducciones `en` y `pt-BR`.
- `period`: el día de negocio en `America/Mexico_City`; los indicadores `daily` usan la fecha de servicio de la boleta
  o la fecha local del incidente. Nunca se publican días futuros. `observedAt` va en UTC.
- `link`: como módulo, la ruta del Shell (`/brocal/personal/matriz`, `/brocal/boletas`, `/brocal/ordenes`,
  `/brocal/hse/incidentes`); independiente, la URL pública `Indicators:PublicUrl` (`BROCAL_PUBLIC_URL` en los compose)
  si es `https://`, si no, sin enlace. `permissionCode`: `brocal.access` como módulo; ninguno independiente (el Shell no
  conoce los permisos de una aplicación independiente).

**Cuándo se envían.**

1. **Definiciones**: `PUT /api/indicators/sources/brocal` al arrancar, reintentado cada minuto hasta que el Shell las
   acepta, y otra vez cada día (idempotente). Mientras no se registran, los valores esperan en el buzón.
2. **Cambios**: al guardar una boleta, orden de trabajo, incidente, locación (enlace, desenlace, baja), documento de
   cumplimiento, tipo de documento, requisito, cuadrilla, al dar de alta, baja o cambiar la clave de una unidad o
   persona, al cambiar el estado o el fin de la prueba de una empresa, o cuando la réplica activa o desactiva un pozo o
   instalación, la capa de persistencia agrega en **la misma transacción** una marca en `indicator_messages` con las
   locaciones, sujetos y días afectados (antes y después del cambio). Cada `Indicators:DispatchSeconds` (60 s) el
   servicio en segundo plano junta las marcas pendientes, recalcula esos sujetos y guarda las solicitudes
   `PUT /api/indicators/values` en lotes de hasta 500 valores que **nunca parten un sujeto** entre dos solicitudes.
3. **Republicación diaria**: una vez por día de negocio (y al arrancar) se encola el valor del día de **cada sujeto
   publicable** (regla de arriba), aunque no haya cambiado: la alerta de «dato sin actualizar» del Shell depende de
   ello. En la misma corrida se retiran los sujetos que se publicaron antes y ya no son publicables (por ejemplo, una
   empresa cuya prueba venció sin que nadie la tocara).
4. **Retiro**: cuando un sujeto deja de ser publicable se envía
   `DELETE /api/indicators/values/brocal/{clave}/{tipo}/{id}` por cada indicador (valor actual e historia); un 404
   cuenta como hecho. Si vuelve a ser publicable, se publica de nuevo.

**Entrega.** En orden de creación con la cuenta de servicio; una ronda se detiene en la primera solicitud que falla
para que un retiro nunca adelante a una publicación anterior. Sin respuesta, 401, 403 o 5xx: se reintenta con espera
creciente (1, 2, 4… hasta 30 minutos); con 401 o 403 se pide un token nuevo, y el detalle de un 403 recuerda que la
cuenta de servicio necesita el rol `chronolith-module-registrar`. Los **retiros** y las publicaciones que llevan **días anteriores a hoy** (una
boleta o un incidente con fecha pasada) se reintentan **hasta entregarse**, porque nada más los repetiría; solo las
publicaciones del día quedan como fallidas tras 10 intentos (la siguiente marca o la republicación diaria vuelve a
publicar el sujeto). Un **404 al publicar** significa que el Shell perdió la fuente (base restaurada, fuente borrada):
no cuenta como intento y las definiciones se registran otra vez antes de seguir entregando. Un **400** (o 422) se
registra en la bitácora de la API y **se descarta sin reintentar**, como la línea de tiempo; si la solicitud llevaba
varios sujetos (por ejemplo, uno que se borró en el Shell), antes se divide en una solicitud por sujeto, en el mismo
lugar de la cola, para descartar solo la del culpable. Se depuran los mensajes **terminados** (entregados, rechazados
o fallidos) de más de 7 días; los pendientes nunca. Varias réplicas de la API se coordinan con un *advisory lock* de
PostgreSQL. Las consultas del cálculo usan índices por `site_id` (boletas, órdenes, incidentes), por
`shell_catalog_id` (locaciones) y por persona o unidad (documentos de cumplimiento).

## Puesta en marcha (desarrollo)

1. ChronoLith en ejecución: `ChronoLith/infra` (Keycloak en 8080, red `chronolith-net`) y `ChronoLith/shell`
   (API en 5080, web en 8081).
2. Clientes de Keycloak (una vez; idempotente):
   ```powershell
   ./scripts/ensure-keycloak-clients.ps1            # o ./scripts/ensure-keycloak-clients.sh (curl + jq)
   ```
   Crea `chronolith-brocal-api` (cuenta de servicio con `chronolith-module-registrar` y audiencia
   `chronolith-api`), `chronolith-brocal-web` (página independiente, PKCE) y el rol de realm
   `brocal-platform-admin`. Con `-ProvisionUsers` (o `BROCAL_PROVISION_USERS=true`) la cuenta de servicio recibe
   además `manage-users`, `view-users` y `query-users` para crear la cuenta de quien se invita.
3. `docker compose up -d --build`, o `./scripts/dev-up.ps1 -Start` para depurar la API (http://localhost:5303) y
   Angular (http://localhost:4303) localmente.
4. En el Shell, Brocal aparece en el menú bajo `/brocal`. El administrador de ChronoLith asigna `brocal.access` a
   los roles que deban usarlo.
5. La empresa demo ("Servicios Petroleros del Sureste") se crea con `BROCAL_SEED_DEMO=true`; su administrador es
   la cuenta de ChronoLith con el correo `BROCAL_DEMO_ADMIN_EMAIL` (por omisión `admin@chronolith.local`).

Puertos de Brocal: web 4303, API 5303 (desarrollo), PostgreSQL 5436. No chocan con Actividades (4301) ni Aforo
(4302).

## Producción (detrás de chronolith-gateway)

1. `.env` a partir de `.env.production.example` (URLs públicas, secreto del cliente, llave de firma de archivos).
2. Clientes de Keycloak contra el Keycloak público:
   ```bash
   KEYCLOAK_PUBLIC_URL=https://chronolith.com.mx/auth KEYCLOAK_ADMIN_PASSWORD=... \
   KC_BROCAL_API_CLIENT_SECRET=... BROCAL_PUBLIC_URL=https://chronolith.com.mx/modules/brocal \
   ./scripts/ensure-keycloak-clients.sh
   ```
3. Agregar Brocal a `ChronoLith/deploy/gateway.conf`, junto a los demás módulos:
   ```nginx
   # Módulo Brocal: remoteEntry.json, bundles y su API (/api). Fotos y documentos de hasta 25 MB.
   location /modules/brocal/ {
       client_max_body_size 30m;
       set $brocal http://chronolith-brocal-web:8080;
       rewrite ^/modules/brocal/(.*)$ /$1 break;
       proxy_pass $brocal;
   }
   ```
4. `docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build`

Con `BROCAL_PUBLIC_URL=https://chronolith.com.mx/modules/brocal`, el Shell descarga el remote desde el gateway y la
API queda en `/modules/brocal/api` (mismo origen que el remote). El contenedor web escucha en el puerto 8080 (nginx
sin privilegios).

## Modo independiente

`docker-compose.standalone.yml` levanta Keycloak propio (realm `brocal` con el tema de login de ChronoLith),
PostgreSQL, API y web, sin ChronoLith ni la red `chronolith-net`. En este modo los permisos de plataforma salen del
rol de realm `brocal-platform-admin`; cualquier usuario autenticado entra a las empresas en las que tiene membresía.
Con `SHELL_API_URL` apuntando a la API de ChronoLith, la opción "Usar el estilo de ChronoLith" toma el tema
predeterminado del Shell; sin ella se usa la paleta de ChronoLith incluida en Brocal. Con `SHELL_API_URL` **y** una
cuenta de servicio del realm `chronolith` (`SHELL_KEYCLOAK_URL`, `SHELL_REALM`, `SHELL_CLIENT_ID`,
`SHELL_CLIENT_SECRET`; un cliente confidencial con *service accounts* y el rol `chronolith-module-registrar`, como el
que crea `scripts/ensure-keycloak-clients.ps1`), Brocal replica además los catálogos maestros del Shell y envía los
cambios de locaciones y clientes enlazados, igual que en modo módulo. Sin esas variables, los catálogos son solo de
Brocal. Ver [despliegue](despliegue.md).
En este modo la propia API valida la licencia de ChronoLith (por omisión, la de desarrollo, válida con Keycloak en
`localhost`); ver la sección *Licencia* del [README](../README.md#licencia-de-chronolith-modo-independiente).

En producción, el modo independiente corre en el servidor de ChronoLith sin Keycloak propio: usa el de ChronoLith con
un realm `brocal` aparte (cuentas separadas) y se publica en https://brocal.chronolith.com.mx. Ver
[producción](produccion.md).
