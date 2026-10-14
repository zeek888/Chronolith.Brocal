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
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` | Correo de los avisos de Brocal y de "¿Olvidaste tu contraseña?". |

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

Compose lee `deploy/.env` por sí solo. La API aplica las migraciones de la base de datos al arrancar.

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
2. Compila las imágenes de la web y de la API, de una en una. Antes exige 2 GB libres en el disco de Docker
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

## Problemas frecuentes

| Síntoma | Revisa |
|---|---|
| `network chronolith-net declared as external, but could not be found` | ChronoLith no está arriba: levanta primero `infra` de ChronoLith. |
| 502 en brocal.chronolith.com.mx | `curl http://127.0.0.1:8094/health` y `docker compose -f deploy/docker-compose.yml ps`. |
| Se ve otro sitio o un certificado equivocado | El sitio no está en `sites-enabled` o falta `sudo systemctl reload nginx`; `sudo nginx -T \| grep brocal`. |
| Keycloak: «Invalid parameter: redirect_uri» | `BROCAL_PUBLIC_URL` en `deploy/.env`; vuelve a ejecutar `ensure-realm.sh`. |
| Error de CORS al regresar del inicio de sesión | *Web origins* del cliente `brocal-web` debe ser `https://brocal.chronolith.com.mx` (lo pone `ensure-realm.sh`). |
| Todas las llamadas responden 401 | `KEYCLOAK_PUBLIC_URL` debe ser el emisor exacto (`https://chronolith.com.mx/auth`) y `KEYCLOAK_INTERNAL_URL` terminar en `/auth`; revisa el log de `brocal-api`. |
| Al invitar: «Keycloak no responde» o «rechazó la cuenta» | Secreto distinto entre `deploy/.env` y el cliente `brocal-api` (vuelve a ejecutar `ensure-realm.sh` y recrea la API); la contraseña temporal debe cumplir la política. |
| No llegan los correos de Brocal | `SMTP_*` en `deploy/.env` (y `up -d brocal-api`); Configuración → Notificaciones → Envíos muestra el error. |
| "¿Olvidaste tu contraseña?" no envía el correo | SMTP del realm: ejecuta `ensure-realm.sh` con `SMTP_HOST`; prueba en la consola (realm `brocal` › *Realm settings* › *Email* › *Test connection*). |

## Limitaciones conocidas

- La página de inicio de sesión está en `chronolith.com.mx/auth` (el dominio de Keycloak) con el tema de ChronoLith.
- La consola de Keycloak es pública en `/auth/admin`, como indica `docs/deploy.md` de ChronoLith: usa contraseñas
  fuertes o restringe esa ruta por IP en su gateway.
