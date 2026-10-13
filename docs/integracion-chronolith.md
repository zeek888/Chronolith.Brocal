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
| Catálogos compartidos | Pozos, campos, instalaciones y compañías del Shell (`GET /api/catalogs/shell/{catalogo}`) para ligar locaciones y clientes. |
| Errores | ProblemDetails con `detail` en español; los muestra el interceptor de errores del Shell. |

El manifiesto declara el menú del Shell (Inicio, Boletas de servicio, Órdenes de trabajo, Dashboard gerencial y,
para administradores, Administración de la plataforma), los dos permisos y los tres widgets, en español e inglés.
Dentro de Brocal, una banda superior muestra el selector de empresa y el menú completo según los permisos.

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
predeterminado del Shell; sin ella se usa la paleta de ChronoLith incluida en Brocal. Ver [despliegue](despliegue.md).

En producción, el modo independiente corre en el servidor de ChronoLith sin Keycloak propio: usa el de ChronoLith con
un realm `brocal` aparte (cuentas separadas) y se publica en https://brocal.chronolith.com.mx. Ver
[producción](produccion.md).
