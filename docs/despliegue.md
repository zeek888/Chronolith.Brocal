# Despliegue

Brocal se distribuye como dos imágenes y una base de datos PostgreSQL 17:

| Imagen | Contenido | Puerto |
|---|---|---|
| `chronolith/brocal-api` | API .NET 10 (`backend/Dockerfile`): ASP.NET 10, `fonts-liberation` para los PDF, `curl` para el *healthcheck*, usuario sin privilegios. Archivos y llaves en el volumen `/data`. | 8080 |
| `chronolith/brocal-web` | nginx sin privilegios (`frontend/Dockerfile`): remote de Native Federation, página independiente y proxy de `/api`. Al arrancar escribe `config.json` con las variables de entorno: la misma imagen sirve en todos los ambientes. | 8080 |

El build de la imagen web verifica las traducciones (`npm run i18n:check`) y compila en producción. El de la API
restaura los paquetes de licenciamiento de ChronoLith desde `backend/nuget/` (fuente local de `backend/nuget.config`;
los `.nupkg` van en git).

## Composiciones

| Archivo | Uso |
|---|---|
| `docker-compose.yml` | Módulo de ChronoLith: `brocal-db`, `brocal-api`, `brocal-web` unidos a la red externa `chronolith-net`. Variables en `.env` (ver `.env.example`). |
| `docker-compose.prod.yml` | Se combina con el anterior en el servidor: sin puertos publicados, alias `chronolith-brocal-web` para el gateway y variables obligatorias (ver `.env.production.example`). |
| `docker-compose.standalone.yml` | Brocal independiente con su Keycloak (realm `brocal`, tema de login de ChronoLith) y su PostgreSQL. Variables en `standalone.env` (copia de `.env.standalone.example`), siempre con `--env-file`. |
| `deploy/docker-compose.yml` | **Producción independiente** en el servidor de ChronoLith (https://brocal.chronolith.com.mx): sin Keycloak propio, usa el de ChronoLith con el realm `brocal`. Variables en `deploy/.env`. Ver [producción](produccion.md). |

```bash
# Módulo (desarrollo)
docker compose up -d --build
# Módulo (producción, detrás de chronolith-gateway)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
# Independiente
docker compose -f docker-compose.standalone.yml --env-file standalone.env up -d --build
# Independiente en producción (brocal.chronolith.com.mx, Keycloak de ChronoLith): ver produccion.md
docker compose -f deploy/docker-compose.yml up -d --build
```

El modo independiente no debe leer el `.env` de la carpeta: es el del modo módulo y apunta al Keycloak de
ChronoLith (`KEYCLOAK_PUBLIC_URL`). Compose carga `.env` por omisión, por eso el independiente usa `--env-file`.

La API aplica las migraciones al arrancar (`Database__ApplyMigrationsOnStartup`) y, si se pide, crea la empresa de
demostración. Salud: `GET /health` en la API y en el contenedor web.

## Configuración de la API

Todas las claves se pueden dar como variables de entorno con `__` (p. ej. `Keycloak__Realm`).

| Clave | Descripción |
|---|---|
| `ConnectionStrings:BrocalDb` | Cadena de conexión de PostgreSQL (esquema `brocal`). |
| `Database:ApplyMigrationsOnStartup` · `SeedDemoTenant` · `DemoAdminEmail` | Migraciones al arrancar; empresa demo y correo de su administrador. |
| `Tenancy:Mode` | `ChronoLith` (módulo) o `Standalone`. |
| `Tenancy:RequireVerifiedEmail` | Las invitaciones solo se vinculan a cuentas con correo verificado (recomendado `true`). |
| `Keycloak:AuthServerUrl` | URL pública de Keycloak (emisor de los tokens). |
| `Keycloak:InternalAuthServerUrl` | URL de Keycloak dentro de Docker para metadatos y llamadas de servidor (p. ej. `http://keycloak:8080`). |
| `Keycloak:Realm` · `Audiences` | `chronolith` / `chronolith-api` como módulo; `brocal` / `brocal-api` independiente. |
| `Keycloak:RequireHttpsMetadata` | `true` en producción. |
| `Keycloak:PlatformAdminRoles` | Roles de realm con administración de la plataforma (`chronolith-admin`, `brocal-platform-admin`). |
| `Keycloak:ClientId` · `ClientSecret` | Cuenta de servicio de la API (registro en el Shell y alta de usuarios). |
| `Shell:ApiUrl` | API del Shell para permisos, tema y catálogos compartidos (vacío en modo independiente sin ChronoLith). |
| `Shell:RequireAccessPermission` · `PermissionsCacheSeconds` | Exigir `brocal.access` del Shell; caché de permisos. |
| `Shell:AuthServerUrl` · `Realm` · `ClientId` · `ClientSecret` | Solo modo independiente conectado a un Shell: cuenta de servicio del realm `chronolith` (rol `chronolith-module-registrar`) que consulta el feed de cambios y envía al maestro. Como módulo se usa la cuenta de `Keycloak:ClientId`. Vacíos = sin réplica de catálogos. |
| `Jobs:CatalogFeedMinutes` | Cada cuántos minutos se consulta el feed de cambios de los catálogos del Shell (2; el contrato pide de 1 a 5). |
| `Indicators:Enabled` · `PublicUrl` · `DispatchSeconds` | Indicadores publicados en el Shell con la misma conexión (`true`; ver [integración](integracion-chronolith.md#indicadores-para-la-gerencia)); URL pública `https://` de Brocal independiente para el enlace de cada valor (`BROCAL_PUBLIC_URL` en los compose independientes; como módulo el enlace es la ruta del Shell); cada cuántos segundos se procesa el buzón `indicator_messages` (60). |
| `ModuleRegistration:Enabled` · `RemoteEntryUrl` · `ApiUrl` · `RoutePath` · `SortOrder` | Manifiesto publicado en el Shell (URLs públicas del módulo). |
| `Identity:ProvisionUsers` | Crear en Keycloak la cuenta de quien se invita (requiere roles de `realm-management`). |
| `Files:RootPath` · `SigningKey` · `DefaultUrlLifetimeMinutes` | Carpeta de archivos (`/data/files` en Docker), llave HMAC de los enlaces (32+ caracteres) y vigencia de los enlaces. |
| `DataProtection:KeysPath` | Llaves que cifran contraseñas de PAC, SMTP y el token de WhatsApp (`/data/keys` en Docker). |
| `Pdf:FontsPath` | Carpeta con fuentes TrueType si no se usan Liberation/DejaVu del sistema. |
| `Email:*` | SMTP de la plataforma (`Host`, `Port`, `UseSsl`, `User`, `Password`, `FromAddress`, `FromName`). |
| `WhatsApp:ApiBaseUrl` · `ApiVersion` | Graph API de Meta (las credenciales son por empresa). |
| `Jobs:Enabled` · `ScanIntervalMinutes` | Envío de la bandeja de salida, revisiones programadas (vencimientos, recordatorios y autorizaciones atrasadas; cada 60 min, mínimo 5), sincronización de los catálogos de ChronoLith y publicación de indicadores. Con `false`, el administrador de la plataforma aún puede ejecutarlas desde **Tareas programadas** y **Catálogos de ChronoLith**. |
| `Cors:AllowedOrigins` | Origen del Shell y de la página independiente. |
| `OpenApi:Enabled` | Publica `/openapi` y `/scalar` fuera de desarrollo. |
| `License:Key` · `License:File` | Solo modo independiente: licencia de ChronoLith (token, o ruta a un archivo con el token que se relee cada 5 min). Sin una válida la API no arranca. En Compose, `License__Key` sale de `LICENSE_KEY`; `docker-compose.standalone.yml` trae por omisión la de desarrollo (solo válida con Keycloak en `localhost`). Ver [producción](produccion.md#licencia). |

## Configuración del contenedor web

| Variable | Descripción |
|---|---|
| `BROCAL_API_UPSTREAM` | Dirección interna de la API (`http://brocal-api:8080`). |
| `KEYCLOAK_URL` · `KEYCLOAK_REALM` · `KEYCLOAK_CLIENT_ID` | Sesión de la página independiente (`chronolith-brocal-web` o `brocal-web`). |
| `CHRONOLITH_URL` | Enlace "Abrir ChronoLith" (opcional). |

`remoteEntry.json`, `importmap.json`, `index.html` y `config.json` se sirven sin caché y con CORS para el Shell;
los archivos con *hash*, con caché de un año.

## Tema de inicio de sesión (modo independiente)

El tema `infra/keycloak/themes/chronolith` se monta en el Keycloak independiente. Keycloak guarda copias
comprimidas de los CSS y JS en `/opt/keycloak/data/tmp/kc-gzip-cache` y no las renueva al cambiar los archivos:
después de editar el tema, bórralas (o recrea el contenedor) para que los navegadores reciban la versión nueva.

```bash
docker exec brocal-standalone-brocal-keycloak-1 rm -rf /opt/keycloak/data/tmp/kc-gzip-cache
```

## Lista para producción

- [ ] Modo independiente: `LICENSE_KEY` con la licencia emitida para la instalación (la de desarrollo se rechaza
  fuera de `localhost`).
- [ ] Contraseñas y secretos propios: base de datos, `KC_BROCAL_API_CLIENT_SECRET`, administrador de Keycloak.
- [ ] `BROCAL_FILES_SIGNING_KEY` aleatoria de 32+ caracteres (cambiarla invalida los enlaces ya emitidos).
- [ ] HTTPS de extremo a extremo y `Keycloak__RequireHttpsMetadata=true`.
- [ ] `BROCAL_SEED_DEMO=false` y `Tenancy__RequireVerifiedEmail=true`.
- [ ] PAC real implementado y configurado por empresa (el PAC "Simulado" no tiene validez fiscal).
- [ ] SMTP de la plataforma o de cada empresa; WhatsApp con plantilla aprobada si se usa.
- [ ] Si se escala la API: `/data` compartido. La bandeja de salida y las revisiones programadas usan candados de
  PostgreSQL (nunca corren dos a la vez), así que basta con `Jobs__Enabled=true` en una réplica; activarlo en
  varias no duplica avisos.

## Respaldos

Respaldar **juntos** la base de datos y el volumen `/data`:

- `pg_dump` de `chronolith_brocal` (todas las empresas viven en el esquema `brocal`).
- `/data/files`: fotos, firmas, PDF y XML timbrados (los CFDI deben conservarse 5 años).
- `/data/keys`: sin estas llaves no se pueden descifrar las contraseñas de PAC y SMTP guardadas por las empresas.
