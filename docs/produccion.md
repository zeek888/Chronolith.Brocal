# Producción: brocal.chronolith.com.mx

Brocal corre en el servidor de ChronoLith (`74.208.194.199`) en **modo independiente**: página propia en
https://brocal.chronolith.com.mx, con su base de datos y sus archivos. Inicia sesión con el **Keycloak de
ChronoLith**, en un realm propio, `brocal`, con cuentas separadas de las de ChronoLith. El Nginx del servidor
termina TLS, igual que para chronolith.com.mx.

| Qué | URL |
|---|---|
| Brocal | https://brocal.chronolith.com.mx |
| Inicio de sesión (Keycloak de ChronoLith, realm `brocal`) | https://chronolith.com.mx/auth/realms/brocal/account |
| Consola de Keycloak | https://chronolith.com.mx/auth/admin (realm `brocal`) |

```
Internet ──80/443──▶ Nginx del servidor (TLS, certbot)
                      ├─ chronolith.com.mx ────────▶ 127.0.0.1:8090 chronolith-gateway ─▶ /auth/ Keycloak (realms chronolith y brocal)
                      └─ brocal.chronolith.com.mx ─▶ 127.0.0.1:8094 brocal-web ─▶ /api ─▶ brocal-api ─▶ brocal-db (PostgreSQL 17)
                                                                                         └─ chronolith-net ─▶ Keycloak (llaves y alta de cuentas)
```

- Brocal solo publica `brocal-web` en `127.0.0.1:8094`; la base de datos no tiene puertos.
- El navegador inicia sesión en `https://chronolith.com.mx/auth` (realm `brocal`, tema de ChronoLith) y regresa a
  Brocal. La API valida los tokens con las llaves que descarga de Keycloak por la red interna `chronolith-net`.
- No cambia nada de ChronoLith (ni su gateway ni su sitio de Nginx): solo se agrega el realm `brocal` a su Keycloak.

> **Antes de actualizar el servidor a la versión con licencias, configura `LICENSE_KEY` en `deploy/.env`.** Desde
> esa versión la API de Brocal **no arranca sin una licencia de ChronoLith válida**. `deploy/update.sh` se detiene
> antes de compilar si no hay `LICENSE_KEY` ni una licencia cargada desde la pantalla Licencia; si actualizas a
> mano, revísalo tú. Ver [Licencia](#licencia).

Archivos de este despliegue, en [deploy/](../deploy):

| Archivo | Para qué |
|---|---|
| `deploy/docker-compose.yml` | Base de datos, API y web de Brocal (proyecto `brocal`). |
| `deploy/.env.example` | Variables del servidor; se copia a `deploy/.env`. |
| `deploy/keycloak/ensure-realm.sh` | Crea o actualiza el realm `brocal` en el Keycloak de ChronoLith. |
| `deploy/nginx/brocal.chronolith.com.mx.conf` | Sitio para el Nginx del servidor. |
| `deploy/update.sh` | Actualiza Brocal: código, respaldo de la base, imágenes y comprobación; `--revertir` vuelve a la versión anterior. |

## Requisitos

- ChronoLith funcionando en el servidor (ver `docs/deploy.md` de ChronoLith): Keycloak en
  `https://chronolith.com.mx/auth` y la red `chronolith-net`.
- DNS: `brocal.chronolith.com.mx` apunta a `74.208.194.199` (ya configurado).
- Docker Compose v2 y `certbot` con el plugin de Nginx (los que ya usa ChronoLith).
- Alrededor de **1 GB de RAM libre** para Brocal en ejecución y **10 GB de disco** (imágenes y datos; las fotos y
  los PDF crecen con el uso). Compilar las imágenes en el servidor pide hasta **4 GB de RAM** durante unos minutos
  (sobre todo la compilación de Angular): si va justo, activa swap.

## 0. Comprobaciones previas (solo lectura)

```bash
docker compose version
docker network inspect chronolith-net --format '{{.Name}}'      # chronolith-net
docker ps --filter label=com.docker.compose.project=chronolith-infra --format '{{.Names}}'   # Keycloak de ChronoLith
sudo ss -tlnp | grep ':8094 '                                     # vacío = el puerto está libre
free -h && df -h /
```

Si el 8094 está ocupado, elige otro: defínelo en `BROCAL_WEB_PORT` (`deploy/.env`) y usa el mismo en el
`proxy_pass` del sitio de Nginx.

## 1. Código

```bash
sudo mkdir -p /opt/brocal && sudo chown "$USER" /opt/brocal
git clone https://github.com/zeek888/Chronolith.Brocal.git /opt/brocal
cd /opt/brocal
```

Si el repositorio es privado, clónalo con una llave de despliegue o un token de solo lectura. Todos los comandos
siguientes se ejecutan desde `/opt/brocal`.

## 2. Variables de entorno

```bash
cp deploy/.env.example deploy/.env
openssl rand -hex 32    # genera un secreto; repítelo para cada uno
```

Las URLs ya apuntan a `brocal.chronolith.com.mx` y al Keycloak de ChronoLith. Edita `deploy/.env`:

| Variable | Valor |
|---|---|
| `KC_BROCAL_API_CLIENT_SECRET` | Secreto nuevo; el script del paso 3 lo registra en Keycloak. |
| `BROCAL_DB_PASSWORD` | Contraseña nueva de la base de datos. |
| `BROCAL_FILES_SIGNING_KEY` | 32 o más caracteres aleatorios (firma de los enlaces a fotos y PDF). |
| `BROCAL_ADMIN_EMAIL` | Correo del primer administrador de la plataforma. |
| `LICENSE_KEY` | Token de la licencia de ChronoLith (ver [Licencia](#licencia)). Obligatoria la primera vez: sin licencia la API no arranca. |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` | Correo de los avisos de Brocal y de "¿Olvidaste tu contraseña?". |
| `SHELL_API_URL`, `SHELL_KEYCLOAK_URL`, `SHELL_REALM`, `SHELL_CLIENT_ID`, `SHELL_CLIENT_SECRET`, `CATALOG_FEED_MINUTES` | Opcionales: catálogos compartidos de ChronoLith (ver [Catálogos de ChronoLith](#catálogos-de-chronolith)). Vacíos, Brocal usa solo sus catálogos. |

Evita `$ ; " \` y espacios en contraseñas y secretos. Muchos proveedores de correo exigen que `SMTP_FROM` sea la
misma cuenta de `SMTP_USER` o un alias suyo.

## 3. Realm en el Keycloak de ChronoLith

```bash
bash deploy/keycloak/ensure-realm.sh
```

- Pide la contraseña del administrador del realm `master` de Keycloak (el permanente que se creó después del
  primer despliegue de ChronoLith). Si no se llama `admin`: `KC_ADMIN_USER=<usuario> bash deploy/keycloak/…`.
- Crea el realm `brocal` con el tema de inicio de sesión de ChronoLith, el rol `brocal-platform-admin` y dos
  clientes:
  - `brocal-web`: cliente público con PKCE que solo acepta `https://brocal.chronolith.com.mx`;
  - `brocal-api`: cuenta de servicio con el secreto de `deploy/.env`, que crea las cuentas al invitar usuarios.
- Copia el SMTP de `deploy/.env` al realm, para que funcione "¿Olvidaste tu contraseña?".
- Con `BROCAL_ADMIN_EMAIL` crea el primer administrador de la plataforma: pide su nombre y una contraseña temporal,
  que deberá cambiar al entrar.
- No crea los usuarios de demostración ni el cliente `brocal-dev-cli` (contraseña directa) del modo de desarrollo.
- Se puede repetir: vuelve a ejecutarlo si cambias la URL, el secreto o el SMTP. Para otro administrador:
  `bash deploy/keycloak/ensure-realm.sh otro@correo.com`.

## 4. Levantar Brocal

```bash
docker compose -f deploy/docker-compose.yml up -d --build --wait
curl -s http://127.0.0.1:8094/health                          # ok
docker compose -f deploy/docker-compose.yml logs brocal-api | tail -n 20
```

Compose lee `deploy/.env` por sí solo. La API aplica las migraciones de la base de datos al arrancar y después
carga la licencia; si no hay una válida, termina y la bitácora dice por qué y cuál es el id de la instalación (ver
[Licencia](#licencia)).

## 5. Nginx y certificado

Respalda antes `/etc/nginx` (por ejemplo `sudo tar czf ~/nginx-$(date +%F).tgz /etc/nginx`). El sitio de Brocal es
un archivo aparte con su propio `server_name`: no toca los de chronolith.com.mx ni sap-pozos.

```bash
sudo cp deploy/nginx/brocal.chronolith.com.mx.conf /etc/nginx/sites-available/brocal.chronolith.com.mx
sudo ln -s /etc/nginx/sites-available/brocal.chronolith.com.mx /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
curl -sI http://brocal.chronolith.com.mx/ | head -n 1       # 200: ya llega a Brocal por http
```

Después emite el certificado. `certbot` agrega al mismo archivo `listen 443 ssl`, el certificado y la redirección de
http a https, y recarga Nginx:

```bash
sudo certbot --nginx -d brocal.chronolith.com.mx --redirect
```

La renovación es automática, con el mismo temporizador de certbot que renueva los demás certificados. Compruébalo
con `sudo certbot renew --dry-run`.

## 6. Verificación

```bash
curl -sI https://brocal.chronolith.com.mx/ | head -n 1                         # 200
curl -sI http://brocal.chronolith.com.mx/ | grep -i '^location'                # https://brocal.chronolith.com.mx/
curl -s  https://brocal.chronolith.com.mx/config.json                          # Keycloak …/auth, realm brocal, cliente brocal-web
curl -s  https://chronolith.com.mx/auth/realms/brocal/.well-known/openid-configuration | grep -o '"issuer":"[^"]*"'
curl -s  -o /dev/null -w '%{http_code}\n' https://brocal.chronolith.com.mx/api/me/context   # 401: la API responde y pide sesión
```

Después abre https://brocal.chronolith.com.mx y entra con `BROCAL_ADMIN_EMAIL` y la contraseña temporal; Keycloak
pide una nueva. Debe aparecer **Administración de la plataforma**. Comprueba también que **chronolith.com.mx y
sap-pozos siguen funcionando**.

## Después del primer despliegue

- **Primera empresa.** Administración de la plataforma → Empresas → Nueva empresa, con su administrador (ver
  [administración](administracion.md)). Al invitar, Brocal crea la cuenta en el realm `brocal` con una contraseña
  temporal.
- **Más administradores de la plataforma.** `bash deploy/keycloak/ensure-realm.sh correo@dominio`, o en la
  consola de Keycloak: realm `brocal` › *Users* › *Role mapping* › `brocal-platform-admin`.
- **Facturación.** El PAC "Simulado" no tiene validez fiscal: configura el PAC real de cada empresa antes de timbrar.
- **Datos de demostración (opcional).** Con `BROCAL_SEED_DEMO=true` en `deploy/.env` y
  `docker compose -f deploy/docker-compose.yml up -d brocal-api`, se crea la empresa "Servicios Petroleros del
  Sureste" (su administrador es `BROCAL_ADMIN_EMAIL`). Después vuelve a dejarlo en `false`.
- **Tareas programadas.** La revisión de vencimientos corre cada hora; su bitácora está en Administración de la
  plataforma → Tareas programadas.

## Operación

**Actualizar:**

```bash
cd /opt/brocal && bash deploy/update.sh
```

El script ([deploy/update.sh](../deploy/update.sh)):

1. Trae el código (`git pull --ff-only`). Se detiene si el servidor tiene cambios locales en archivos del
   repositorio; un `chmod` no cuenta. Si el pull trae otra versión del script, sigue con la nueva.
2. Comprueba que haya licencia: `LICENSE_KEY` en `deploy/.env` o una cargada desde la pantalla Licencia (guardada
   en la base). Sin ninguna se detiene sin cambiar nada. Después compila las imágenes de la web y de la API, de una
   en una. Antes exige 2 GB libres en el disco de Docker
   (`BROCAL_MIN_FREE_GB`) y avisa si hay menos de 4 GB de RAM y swap disponibles (`BROCAL_MIN_MEMORY_GB`), lo que
   puede pedir la compilación de Angular.
3. Si cambió la imagen de la API, respalda la base en `backups/`, porque la API aplica sus migraciones al arrancar.
   Conserva los 10 respaldos más recientes (`BROCAL_BACKUPS_KEEP`; 0 los conserva todos).
4. Recrea solo los contenedores que cambiaron, espera a que estén sanos y comprueba `http://127.0.0.1:8094`
   (página 200, `config.json` 200 y `/api/me/context` 401). Si algo falla, muestra el estado y la bitácora de la
   API, y termina con error.
5. Guarda las imágenes reemplazadas con la etiqueta `anterior`. Las de hace dos actualizaciones se borran si ya no
   las usa nada; las imágenes de otros proyectos del servidor no se tocan.
6. Avisa si cambió `deploy/keycloak/` (ejecuta entonces `ensure-realm.sh`), si cambió `deploy/nginx/` o si
   `deploy/.env.example` trae variables que no están en `deploy/.env`.

Si las imágenes no cambiaron, no hace respaldo y solo recrea los contenedores cuya configuración cambió en
`deploy/.env`.

- Ejecútalo como el usuario dueño de `/opt/brocal`, con acceso a docker y sin `sudo`. En una conexión inestable,
  hazlo dentro de `tmux`; si se corta a medias, vuelve a ejecutarlo.
- `bash deploy/update.sh --sin-pull` compila y despliega el código que ya está en el servidor.
- `bash deploy/update.sh --revertir` intercambia las imágenes actuales con las `anterior`: repetirlo regresa a las
  nuevas. No toca la base ni los archivos. Si la versión nueva aplicó migraciones que la anterior no admite,
  restaura el respaldo de `backups/` previo a la actualización (ver *Respaldos*); se pierde lo que se haya guardado
  después.
- A mano, sin el script (respalda antes la base):

  ```bash
  cd /opt/brocal && git pull
  docker compose -f deploy/docker-compose.yml build brocal-web && docker compose -f deploy/docker-compose.yml build brocal-api
  docker compose -f deploy/docker-compose.yml up -d --wait
  ```

El sitio de Nginx (`/etc/nginx/sites-available/brocal.chronolith.com.mx`) ya incluye los cambios de certbot: si
cambia `deploy/nginx/brocal.chronolith.com.mx.conf`, aplica el cambio a mano en ese archivo en lugar de volver a
copiarlo (`git diff` muestra qué cambió) y ejecuta `sudo nginx -t && sudo systemctl reload nginx`.

**Respaldos.** `deploy/update.sh` respalda la base en `backups/brocal-AAAAMMDD-HHMMSS.dump` antes de cada
actualización de la API (solo el dueño puede leerlos); no respalda los archivos, que la actualización no modifica.
Para un respaldo completo, respalda juntos la base de datos y los archivos:

```bash
# Base de datos (todas las empresas)
docker compose -f deploy/docker-compose.yml exec -T brocal-db pg_dump -U brocal -Fc chronolith_brocal > brocal-$(date +%F).dump

# Fotos, firmas, PDF y XML timbrados (/data/files) y llaves de cifrado (/data/keys)
docker run --rm -v brocal_brocal-data:/data:ro -v "$PWD":/backup alpine tar czf /backup/brocal-data-$(date +%F).tgz -C /data .
```

- Ajusta el usuario de `pg_dump` si cambiaste `BROCAL_DB_USER`.
- Para restaurar la base:
  `docker compose -f deploy/docker-compose.yml exec -T brocal-db pg_restore -U brocal -d chronolith_brocal --clean --if-exists < backups/brocal-AAAAMMDD-HHMMSS.dump`.
- Copia también los respaldos fuera del servidor: `backups/` está en el mismo disco que la base.
- Los CFDI timbrados deben conservarse 5 años.
- Sin `/data/keys` no se pueden descifrar las contraseñas de PAC y SMTP que guardan las empresas.
- Las cuentas de usuario viven en el Keycloak de ChronoLith: su respaldo (`docs/deploy.md` de ChronoLith) incluye el
  realm `brocal`.

**Bitácoras.** `docker compose -f deploy/docker-compose.yml logs -f brocal-api`. Docker conserva hasta 5 archivos
de 10 MB por contenedor.

**Detener Brocal** (ChronoLith no se ve afectado; brocal.chronolith.com.mx responderá 502):

```bash
docker compose -f deploy/docker-compose.yml down
```

`down` conserva los datos. **Nunca** uses `down -v` en producción: borra los volúmenes.

## Catálogos de ChronoLith

Opcional. Con estas variables en `deploy/.env`, Brocal replica los catálogos maestros del Shell de ChronoLith (pozos,
instalaciones, campos, compañías…) por su feed de cambios y envía al maestro los cambios de las locaciones y clientes
enlazados; sin ellas, sigue funcionando con sus propios catálogos. Ambos servicios se alcanzan por `chronolith-net`:

| Variable | Valor en este servidor |
|---|---|
| `SHELL_API_URL` | `http://shell-api:8080` (API del Shell dentro de `chronolith-net`). |
| `SHELL_KEYCLOAK_URL` | `http://chronolith-keycloak:8080/auth` (mismo Keycloak que `KEYCLOAK_INTERNAL_URL`). |
| `SHELL_REALM` | `chronolith` (el realm del Shell, no el `brocal` de los usuarios). |
| `SHELL_CLIENT_ID` / `SHELL_CLIENT_SECRET` | Cliente confidencial del realm `chronolith` con *service accounts* y el rol de realm `chronolith-module-registrar`. Puede ser el `chronolith-brocal-api` que crea `scripts/ensure-keycloak-clients.sh` contra `https://chronolith.com.mx/auth`, con su secreto. |
| `CATALOG_FEED_MINUTES` | Cada cuántos minutos se consulta el feed (2; el contrato pide de 1 a 5). |

Para que la cuenta de servicio pueda **publicar** en el maestro lo que los usuarios no pudieron (buzón de salida), en el
Shell (*Administración → Usuarios*) asigna al usuario `service-account-<SHELL_CLIENT_ID>` un rol con el permiso
`shell.catalogs.manage`. Después de cambiar estas variables, `docker compose -f deploy/docker-compose.yml up -d brocal-api`.
La primera consulta descarga los catálogos completos; el estado se ve en *Administración de la plataforma → Catálogos
de ChronoLith* y en la bitácora de `brocal-api` («Catálogos compartidos: …»). En el Shell, *Administración › Cambios de
catálogos* muestra al consumidor `brocal` con su cursor y sus errores.

Con la misma conexión, Brocal publica sus **indicadores** en el Shell (documentos, boletas, órdenes e incidentes HSE por
pozo o instalación enlazados; ver [integración](integracion-chronolith.md#indicadores-para-la-gerencia)), con enlaces a
`BROCAL_PUBLIC_URL`. La cuenta de servicio solo necesita `chronolith-module-registrar` y la licencia de la plataforma
ChronoLith debe incluir `brocal` para que el Shell los muestre. En la bitácora de `brocal-api` aparecen como
«Indicadores de Brocal: …»; para apagarlos, `Indicators__Enabled=false`.

## Licencia

Brocal independiente valida una **licencia de ChronoLith** (token firmado) emitida para el producto `brocal` (o `*`).
La licencia es de la instalación: todas las empresas la comparten, junto con sus límites. Como módulo de ChronoLith,
en cambio, la valida el Shell y `LICENSE_KEY` no se usa.

**Fuentes.** `LICENSE_KEY` en `deploy/.env` (llega a la API como `License__Key`) y la licencia cargada desde
**Administración de la plataforma → Licencia** (queda guardada en la base). Entre las válidas manda la **emitida más
recientemente**. La API relee la base cada 5 minutos, así que una licencia cargada en la pantalla no requiere
reiniciar.

**Primera instalación o primera actualización a la versión con licencias:**

1. Pide la licencia a ChronoLith. Para la primera instalación puede emitirse una licencia inicial sin id de
   instalación (vale en cualquier servidor, con vigencia corta).
2. Ponla en `deploy/.env` (`LICENSE_KEY=<token>`, en una sola línea) y levanta o actualiza Brocal.
3. Si la API no arranca, su bitácora muestra el motivo y el **id de la instalación**, que se crea en la base la
   primera vez:

   ```bash
   docker compose -f deploy/docker-compose.yml logs brocal-api | grep -i licencia
   ```

4. Con la API arriba, el id también está en la pantalla **Licencia** (botón Copiar). Envíalo a ChronoLith para que
   emita la licencia definitiva de esta instalación y cárgala como se indica en *Renovar*.

**Renovar o cambiar la licencia** (la nueva debe haberse emitido después de la activa):

- Desde Brocal: Administración de la plataforma → **Licencia** → pega el token → *Cargar licencia*. Se activa sin
  reiniciar y se conserva en la base.
- O en `deploy/.env`: cambia `LICENSE_KEY` y recrea la API con
  `docker compose -f deploy/docker-compose.yml up -d brocal-api`.

**Estados.** Por vencer (menos de 30 días): aviso amarillo para todos los usuarios. Vencida dentro del periodo de
gracia (30 días por omisión): todo funciona, con aviso rojo. Terminada la gracia: **solo lectura**; se puede
consultar y exportar, iniciar sesión, marcar notificaciones como leídas y cargar una licencia nueva, pero cualquier
otro cambio responde 403. Una licencia de desarrollo solo se acepta con Keycloak en `localhost`: en este servidor se
rechaza.

**Módulos y límites.** La licencia puede limitar los módulos (productos `brocal.<módulo>`; sin ellos incluye todos) y
fijar límites para toda la instalación: `users`, `portalusers`, `companies`, `units`, `employees`, `tickets` (por
mes), `storagemb` y `apikeys`. Un límite ausente no limita. El uso y los módulos incluidos aparecen en la pantalla
Licencia. Detalle en [administración](administracion.md#licencia-de-chronolith).

Ejemplo de emisión con la herramienta de `ChronoLith.Licensing` (paquete Campo: boletas, órdenes de trabajo y sus
dependencias, 15 usuarios, 20 unidades y 60 empleados):

```powershell
dotnet $tool issue --customer "Servicios del Golfo S.A. de C.V." --instance <id> --expires 2027-11-30 `
  --product brocal:users=15,companies=1,units=20,employees=60 --product brocal.dispatch
```

Una licencia emitida antes de esta versión (sin productos `brocal.*`) sigue incluyendo todos los módulos.

## Problemas frecuentes

| Síntoma | Revisa |
|---|---|
| `network chronolith-net declared as external, but could not be found` | ChronoLith no está arriba: levanta primero `infra` de ChronoLith. |
| 502 en brocal.chronolith.com.mx | `curl http://127.0.0.1:8094/health` y `docker compose -f deploy/docker-compose.yml ps`. |
| `brocal-api` se reinicia y su bitácora dice «No hay una licencia válida de ChronoLith» | Falta `LICENSE_KEY` o no sirve para esta instalación (otro id, otro producto, de desarrollo, incompleta); la misma línea trae el id de la instalación. Ver [Licencia](#licencia). |
| Todo cambio responde «Licencia vencida» (403) | Terminó el periodo de gracia: carga una licencia nueva en Administración de la plataforma → Licencia o en `LICENSE_KEY`. |
| Se ve otro sitio o un certificado equivocado | El sitio no está en `sites-enabled` o falta `sudo systemctl reload nginx`; `sudo nginx -T \| grep brocal`. |
| Keycloak: «Invalid parameter: redirect_uri» | `BROCAL_PUBLIC_URL` en `deploy/.env`; vuelve a ejecutar `ensure-realm.sh`. |
| Error de CORS al regresar del inicio de sesión | *Web origins* del cliente `brocal-web` debe ser `https://brocal.chronolith.com.mx` (lo pone `ensure-realm.sh`). |
| Todas las llamadas responden 401 | `KEYCLOAK_PUBLIC_URL` debe ser el emisor exacto (`https://chronolith.com.mx/auth`) y `KEYCLOAK_INTERNAL_URL` terminar en `/auth`; revisa el log de `brocal-api`. |
| Al invitar: «Keycloak no responde» o «rechazó la cuenta» | Secreto distinto entre `deploy/.env` y el cliente `brocal-api` (vuelve a ejecutar `ensure-realm.sh` y recrea la API); la contraseña temporal debe cumplir la política. |
| No llegan los correos de Brocal | `SMTP_*` en `deploy/.env` (y `up -d brocal-api`); Configuración → Notificaciones → Envíos muestra el error. |
| "¿Olvidaste tu contraseña?" no envía el correo | SMTP del realm: ejecuta `ensure-realm.sh` con `SMTP_HOST`; prueba en la consola (realm `brocal` › *Realm settings* › *Email* › *Test connection*). |
| Catálogos de ChronoLith: «no está conectado» o la réplica no avanza | Faltan `SHELL_API_URL` o la cuenta de servicio (`SHELL_CLIENT_ID`/`SECRET`); revisa el último error en la pantalla y `docker compose -f deploy/docker-compose.yml logs brocal-api \| grep -i catálogos`. Un 401/403 del feed = la cuenta no tiene `chronolith-module-registrar`. |
| Envíos al maestro siempre «pendientes» | La cuenta de servicio no tiene un rol del Shell con `shell.catalogs.manage` (el detalle del envío dice 403). |

## Limitaciones conocidas

- La página de inicio de sesión está en `chronolith.com.mx/auth` (el dominio de Keycloak) con el tema de ChronoLith.
- La consola de Keycloak es pública en `/auth/admin`, como indica `docs/deploy.md` de ChronoLith: usa contraseñas
  fuertes o restringe esa ruta por IP en su gateway.
