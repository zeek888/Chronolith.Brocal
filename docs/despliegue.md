# Despliegue

Brocal se distribuye como dos imágenes y una base de datos PostgreSQL 17:

| Imagen | Contenido | Puerto |
|---|---|---|
| `chronolith/brocal-api` | API .NET 10 (`backend/Dockerfile`): ASP.NET 10, `fonts-liberation` para los PDF, `curl` para el *healthcheck*, usuario sin privilegios. Archivos y llaves en el volumen `/data`. | 8080 |
| `chronolith/brocal-web` | nginx sin privilegios (`frontend/Dockerfile`): remote de Native Federation, página independiente y proxy de `/api`. Al arrancar escribe `config.json` con las variables de entorno: la misma imagen sirve en todos los ambientes. | 8080 |

El build de la imagen web verifica las traducciones (`npm run i18n:check`) y compila en producción.

## Composiciones

| Archivo | Uso |
|---|---|
| `docker-compose.yml` | Módulo de ChronoLith: `brocal-db`, `brocal-api`, `brocal-web` unidos a la red externa `chronolith-net`. Variables en `.env` (ver `.env.example`). |
| `docker-compose.prod.yml` | Se combina con el anterior en el servidor: sin puertos publicados, alias `chronolith-brocal-web` para el gateway y variables obligatorias (ver `.env.production.example`). |
| `docker-compose.standalone.yml` | Brocal independiente con su Keycloak (realm `brocal`, tema de login de ChronoLith) y su PostgreSQL. Variables en `.env.standalone.example`. |

```bash
# Módulo (desarrollo)
docker compose up -d --build
# Módulo (producción, detrás de chronolith-gateway)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
# Independiente
docker compose -f docker-compose.standalone.yml up -d --build
```

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
| `ModuleRegistration:Enabled` · `RemoteEntryUrl` · `ApiUrl` · `RoutePath` · `SortOrder` | Manifiesto publicado en el Shell (URLs públicas del módulo). |
| `Identity:ProvisionUsers` | Crear en Keycloak la cuenta de quien se invita (requiere roles de `realm-management`). |
| `Files:RootPath` · `SigningKey` · `DefaultUrlLifetimeMinutes` | Carpeta de archivos (`/data/files` en Docker), llave HMAC de los enlaces (32+ caracteres) y vigencia de los enlaces. |
| `DataProtection:KeysPath` | Llaves que cifran contraseñas de PAC, SMTP y el token de WhatsApp (`/data/keys` en Docker). |
| `Pdf:FontsPath` | Carpeta con fuentes TrueType si no se usan Liberation/DejaVu del sistema. |
| `Email:*` | SMTP de la plataforma (`Host`, `Port`, `UseSsl`, `User`, `Password`, `FromAddress`, `FromName`). |
| `WhatsApp:ApiBaseUrl` · `ApiVersion` | Graph API de Meta (las credenciales son por empresa). |
| `Jobs:Enabled` · `ScanIntervalMinutes` | Envío de la bandeja de salida y revisiones programadas (60 min). |
| `Cors:AllowedOrigins` | Origen del Shell y de la página independiente. |
| `OpenApi:Enabled` | Publica `/openapi` y `/scalar` fuera de desarrollo. |

## Configuración del contenedor web

| Variable | Descripción |
|---|---|
| `BROCAL_API_UPSTREAM` | Dirección interna de la API (`http://brocal-api:8080`). |
| `KEYCLOAK_URL` · `KEYCLOAK_REALM` · `KEYCLOAK_CLIENT_ID` | Sesión de la página independiente (`chronolith-brocal-web` o `brocal-web`). |
| `CHRONOLITH_URL` | Enlace "Abrir ChronoLith" (opcional). |

`remoteEntry.json`, `importmap.json`, `index.html` y `config.json` se sirven sin caché y con CORS para el Shell;
los archivos con *hash*, con caché de un año.

## Lista para producción

- [ ] Contraseñas y secretos propios: base de datos, `KC_BROCAL_API_CLIENT_SECRET`, administrador de Keycloak.
- [ ] `BROCAL_FILES_SIGNING_KEY` aleatoria de 32+ caracteres (cambiarla invalida los enlaces ya emitidos).
- [ ] HTTPS de extremo a extremo y `Keycloak__RequireHttpsMetadata=true`.
- [ ] `BROCAL_SEED_DEMO=false` y `Tenancy__RequireVerifiedEmail=true`.
- [ ] PAC real implementado y configurado por empresa (el PAC "Simulado" no tiene validez fiscal).
- [ ] SMTP de la plataforma o de cada empresa; WhatsApp con plantilla aprobada si se usa.
- [ ] Una sola réplica con `Jobs__Enabled=true` si se escala la API (el resto con `false`) y `/data` compartido.

## Respaldos

Respaldar **juntos** la base de datos y el volumen `/data`:

- `pg_dump` de `chronolith_brocal` (todas las empresas viven en el esquema `brocal`).
- `/data/files`: fotos, firmas, PDF y XML timbrados (los CFDI deben conservarse 5 años).
- `/data/keys`: sin estas llaves no se pueden descifrar las contraseñas de PAC y SMTP guardadas por las empresas.
