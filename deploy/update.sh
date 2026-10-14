#!/usr/bin/env bash
# Actualiza Brocal en el servidor (deploy/docker-compose.yml, proyecto brocal):
#   1. trae el código de GitHub (git pull --ff-only; se detiene si el servidor tiene cambios locales);
#   2. compila las imágenes de la web y de la API, de una en una;
#   3. si cambió la imagen de la API, respalda la base en backups/ (la API aplica sus migraciones al arrancar);
#   4. recrea solo los contenedores que cambiaron, espera a que estén sanos y comprueba la web y la API en
#      http://127.0.0.1:BROCAL_WEB_PORT;
#   5. guarda las imágenes reemplazadas con la etiqueta «anterior», para volver atrás.
# Al final avisa si cambiaron deploy/nginx/ o deploy/keycloak/, o si deploy/.env.example trae variables que no están
# en deploy/.env.
#
#   bash deploy/update.sh              actualiza
#   bash deploy/update.sh --sin-pull   compila y despliega el código que ya está en el servidor
#   bash deploy/update.sh --revertir   vuelve a las imágenes anteriores sin tocar la base (repetirlo regresa)
#
# Ejecútalo como el usuario dueño de /opt/brocal, con acceso a docker y sin sudo; si la conexión es inestable, dentro
# de tmux. Opcionales: BROCAL_ENV_FILE (deploy/.env), BROCAL_BACKUP_DIR (backups/), BROCAL_BACKUPS_KEEP (10),
# BROCAL_MIN_FREE_GB (2 GB libres en el disco de Docker para compilar) y BROCAL_MIN_MEMORY_GB (avisa con menos de
# 4 GB de RAM y swap disponibles).
set -euo pipefail
umask 077

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${BROCAL_ENV_FILE:-$ROOT/deploy/.env}"
COMPOSE_FILE="$ROOT/deploy/docker-compose.yml"
BACKUP_DIR="${BROCAL_BACKUP_DIR:-$ROOT/backups}"
KEEP="${BROCAL_BACKUPS_KEEP:-10}"
MIN_FREE_GB="${BROCAL_MIN_FREE_GB:-2}"
MIN_MEMORY_GB="${BROCAL_MIN_MEMORY_GB:-4}"
SITE=/etc/nginx/sites-available/brocal.chronolith.com.mx
SERVICES=(brocal-web brocal-api)
declare -A IMAGE=() BEFORE=() AFTER=() CHANGED=()
NOTES=()
BACKUP_FILE=""
KEPT=0
# Commit anterior al git pull (lo pasa la versión previa del script cuando se reinicia con la nueva).
FROM_COMMIT="${BROCAL_UPDATE_FROM:-}"
PULL=true
REVERT=false
WORK=""

die() { echo "$*" >&2; exit 1; }
# Los procesos hijos no heredan el candado (descriptor 9): git pull deja un «git maintenance» en segundo plano que lo
# retendría.
git() { command git "$@" 9>&-; }
docker() { command docker "$@" 9>&-; }
usage() { sed -n '2,/^set -euo pipefail/p' "${BASH_SOURCE[0]}" | sed -e '$d' -e 's/^# \{0,1\}//'; }
# Valor de una variable de deploy/.env, sin ejecutar el archivo (igual que ensure-realm.sh).
env_value() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1 | sed -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'$/\1/"; }
# Nombres de las variables de un archivo .env, sin sus valores.
env_names() { sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$1" | LC_ALL=C sort -u; }
compose() { docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"; }
image_id() { docker image inspect -f '{{.Id}}' "$1" 2>/dev/null || true; }
# Huella del contenido de una imagen (capas y configuración). Con el almacén de imágenes de containerd, el ID cambia en
# cada compilación aunque el contenido sea el mismo (incluye la procedencia que agrega BuildKit): se comparan huellas.
fingerprint() {
  local data=""
  [ -z "$1" ] || data="$(docker image inspect -f '{{json .RootFS.Layers}}{{json .Config}}' "$1" 2>/dev/null || true)"
  [ -z "$data" ] || printf '%s' "$data" | sha256sum | cut -c1-16
}
short() { if [ -n "$1" ]; then local id="${1#sha256:}"; printf '%s' "${id:0:12}"; else printf 'ninguna'; fi; }

# Imagen con la que está creado el contenedor del servicio (si todavía existe) o, si no, la de su etiqueta.
current_image() {
  local container id=""
  container="$(compose ps -aq "$1" 2>/dev/null | head -n 1 || true)"
  [ -z "$container" ] || id="$(docker inspect -f '{{.Image}}' "$container" 2>/dev/null || true)"
  if [ -n "$id" ] && [ -n "$(image_id "$id")" ]; then printf '%s\n' "$id"; else image_id "${IMAGE[$1]}"; fi
}

# Con el almacén de imágenes de containerd (el predeterminado de Docker 29), una imagen que pierde su última etiqueta
# desaparece aunque un contenedor la use. Por eso la imagen en uso lleva la etiqueta temporal «actualizando» mientras
# se compila; si la actualización falla, se queda para la siguiente.
protect() { docker tag "$2" "${IMAGE[$1]%:*}:actualizando"; }
release() { docker image rm "${IMAGE[$1]%:*}:actualizando" >/dev/null 2>&1 || true; }

preflight() {
  [ -f "$ENV_FILE" ] || die "Falta $ENV_FILE: cópialo de deploy/.env.example y complétalo (docs/produccion.md)."
  docker info >/dev/null 2>&1 || die "No puedo usar docker: ¿está en ejecución y tu usuario pertenece al grupo docker?"
  docker compose version >/dev/null 2>&1 || die "Falta Docker Compose v2 (docker compose)."
  compose config -q || die "deploy/docker-compose.yml o $ENV_FILE tienen un error (detalle arriba)."
  docker network inspect chronolith-net >/dev/null 2>&1 || die "No existe la red chronolith-net: levanta primero infra de ChronoLith."

  # Mismo orden que Compose: la variable del entorno, la de deploy/.env o el valor por omisión.
  TAG="${BROCAL_VERSION:-$(env_value BROCAL_VERSION)}"
  TAG="${TAG:-latest}"
  PORT="${BROCAL_WEB_PORT:-$(env_value BROCAL_WEB_PORT)}"
  PORT="${PORT:-8094}"
  PUBLIC="$(env_value BROCAL_PUBLIC_URL)"
  PUBLIC="${PUBLIC:-https://brocal.chronolith.com.mx}"
  # Las mismas etiquetas que image: en deploy/docker-compose.yml.
  IMAGE=([brocal-web]="chronolith/brocal-web:$TAG" [brocal-api]="chronolith/brocal-api:$TAG")

  mkdir -p "$BACKUP_DIR"
  WORK="$(mktemp -d "${TMPDIR:-/tmp}/brocal-update.XXXXXX")"
  # Una sola actualización a la vez.
  if command -v flock >/dev/null 2>&1 && { exec 9>>"$BACKUP_DIR/.update.lock"; } 2>/dev/null; then
    flock -n 9 || die "Ya hay otra actualización de Brocal en curso."
  fi
}

# Compilar pide unos 2 GB más de disco: sin espacio fallaría a medias y afectaría a los demás sitios del servidor.
# La compilación de Angular pide hasta 4 GB de RAM; con menos solo avisa, porque casi siempre alcanza.
check_resources() {
  local root avail mem
  root="$(docker info -f '{{.DockerRootDir}}' 2>/dev/null || true)"
  avail="$(df -Pk "${root:-/}" 2>/dev/null | awk 'NR == 2 { print $4 }' || true)"
  if [ -n "$avail" ] && [ "$avail" -lt $((MIN_FREE_GB * 1024 * 1024)) ]; then
    die "Quedan $((avail / 1024)) MB libres en el disco de Docker y compilar pide unos 2 GB: libera espacio (docker system df) o ajusta BROCAL_MIN_FREE_GB."
  fi
  mem="$(awk '/^(MemAvailable|SwapFree):/ { kb += $2 } END { print kb + 0 }' /proc/meminfo 2>/dev/null || true)"
  if [ "${mem:-0}" -gt 0 ] && [ "$mem" -lt $((MIN_MEMORY_GB * 1024 * 1024)) ]; then
    echo "  · aviso: hay $((mem / 1024)) MB de RAM y swap disponibles y compilar la web pide hasta 4 GB; si la compilación falla, activa swap (docs/produccion.md, Requisitos)"
  fi
}

pull_code() {
  git -C "$ROOT" rev-parse --git-dir >/dev/null ||
    die "No pude usar git en $ROOT (detalle arriba): debe ser el clon de docs/produccion.md, o usa --sin-pull."
  # core.fileMode=false: un chmod +x en el servidor no cuenta como cambio.
  if [ -n "$(git -C "$ROOT" -c core.fileMode=false status --porcelain --untracked-files=no)" ]; then
    git -C "$ROOT" -c core.fileMode=false status --short --untracked-files=no >&2
    die "El servidor tiene cambios locales en archivos del repositorio (arriba): revísalos con git diff y guárdalos o descártalos antes de actualizar, o usa --sin-pull."
  fi
  local old new
  old="$(git -C "$ROOT" rev-parse HEAD)"
  git -C "$ROOT" -c core.fileMode=false pull --ff-only --quiet ||
    die "No pude traer el código (git pull --ff-only): revisa el acceso a GitHub o si la rama del servidor se desvió."
  new="$(git -C "$ROOT" rev-parse HEAD)"
  if [ "$old" = "$new" ]; then
    echo "  · código al día: $(git -C "$ROOT" log -1 --format='%h %s')"
    return 0
  fi
  echo "  · código: $(git -C "$ROOT" rev-list --count "$old..$new") commits nuevos"
  git -C "$ROOT" log -n 15 --format='      %h %s' "$old..$new"
  FROM_COMMIT="$old"
  # Si el pull trajo otra versión de este script, la actualización sigue con ella.
  if ! git -C "$ROOT" diff --quiet "$old" "$new" -- deploy/update.sh; then
    echo "  · deploy/update.sh cambió: sigo con la versión nueva"
    rm -rf "$WORK"
    BROCAL_UPDATE_FROM="$old" exec bash "$ROOT/deploy/update.sh" --sin-pull
  fi
}

build_images() {
  local svc log
  for svc in "${SERVICES[@]}"; do
    log="$WORK/build-$svc.log"
    echo "  · compilando $svc (unos minutos)…"
    if ! compose build "$svc" >"$log" 2>&1; then
      tail -n 30 "$log" >&2
      die "Falló la compilación de $svc: los contenedores siguen como estaban. Bitácora completa: $log"
    fi
    AFTER[$svc]="$(image_id "${IMAGE[$svc]}")"
  done
}

backup_database() {
  if [ -z "$(compose ps -q --status running brocal-db 2>/dev/null || true)" ]; then
    echo "  · la base no está en ejecución (primer despliegue): sin respaldo"
    return 0
  fi
  BACKUP_FILE="$BACKUP_DIR/brocal-$(date +%Y%m%d-%H%M%S).dump"
  # Usuario y base son variables del contenedor de PostgreSQL: se expanden allá.
  # shellcheck disable=SC2016
  if ! compose exec -T brocal-db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' >"$BACKUP_FILE" ||
    ! compose exec -T brocal-db pg_restore -l <"$BACKUP_FILE" >/dev/null 2>&1; then
    rm -f "$BACKUP_FILE"
    die "Falló el respaldo de la base: los contenedores siguen como estaban."
  fi
  echo "  · respaldo de la base: $BACKUP_FILE ($(du -h "$BACKUP_FILE" | cut -f1))"
  # Conserva los KEEP más recientes (0: todos); el nombre lleva la fecha, así que el orden alfabético es el cronológico.
  local files i
  shopt -s nullglob
  files=("$BACKUP_DIR"/brocal-*.dump)
  shopt -u nullglob
  for ((i = 0; KEEP > 0 && i < ${#files[@]} - KEEP; i++)); do rm -f -- "${files[$i]}"; done
}

# La imagen reemplazada queda como «anterior». La de hace dos actualizaciones pierde esa etiqueta: con el almacén
# clásico se borra si ya no tiene etiquetas ni contenedores; con el de containerd desaparece sola. Una imagen con otra
# etiqueta, de este u otro proyecto, no se toca.
keep_previous() {
  local svc="$1" repo="${IMAGE[$1]%:*}" old
  [ -n "${BEFORE[$svc]}" ] || return 0
  old="$(image_id "$repo:anterior")"
  docker tag "${BEFORE[$svc]}" "$repo:anterior"
  release "$svc"
  KEPT=$((KEPT + 1))
  if [ -n "$old" ] && [ "$old" != "${BEFORE[$svc]}" ] &&
    [ "$(docker image inspect -f '{{len .RepoTags}}' "$old" 2>/dev/null || true)" = 0 ]; then
    docker image rm "$old" >/dev/null 2>&1 || true
  fi
}

# /health lo responde el Nginx de la web sin llegar a la API: la API se comprueba con /api/me/context, que sin sesión
# responde 401.
check_http() {
  if ! command -v curl >/dev/null 2>&1; then
    echo "  · sin curl: no comprobé las URLs"
    return 0
  fi
  local base="http://127.0.0.1:$PORT" page config me=""
  for _ in {1..15}; do
    me="$(curl -s -o /dev/null -w '%{http_code}' "$base/api/me/context" || true)"
    [ "$me" = 401 ] && break
    sleep 2
  done
  page="$(curl -s -o /dev/null -w '%{http_code}' "$base/" || true)"
  config="$(curl -s -o /dev/null -w '%{http_code}' "$base/config.json" || true)"
  echo "  · $base: página $page, config.json $config, /api/me/context $me (se esperan 200, 200 y 401)"
  [ "$page" = 200 ] && [ "$config" = 200 ] && [ "$me" = 401 ]
}

fail() {
  echo "$1" >&2
  compose ps >&2 || true
  echo "Últimas líneas de la API:" >&2
  compose logs --no-color --tail 40 brocal-api >&2 || true
  if ! $REVERT && [ -n "$(image_id "${IMAGE[brocal-web]%:*}:anterior")$(image_id "${IMAGE[brocal-api]%:*}:anterior")" ]; then
    echo "Para volver a la versión anterior: bash deploy/update.sh --revertir" >&2
  fi
  [ -z "$BACKUP_FILE" ] || echo "Respaldo de la base previo a esta actualización: $BACKUP_FILE" >&2
  exit 1
}

start_and_check() {
  echo "  · recreando lo que cambió y esperando a que esté sano…"
  if ! compose up -d --wait >"$WORK/up.log" 2>&1; then
    cat "$WORK/up.log" >&2
    fail "Los contenedores no quedaron sanos."
  fi
  check_http || fail "La web o la API no responden como se espera."
}

# 0: sin cambios en esa ruta desde FROM_COMMIT; 1: con cambios; otro: no se pudo comparar.
changed_since_pull() {
  local status=0
  git -C "$ROOT" diff --quiet "$FROM_COMMIT" HEAD -- "$1" 2>/dev/null || status=$?
  [ "$status" -eq 1 ]
}

collect_notes() {
  if [ -n "$FROM_COMMIT" ]; then
    ! changed_since_pull deploy/keycloak ||
      NOTES+=("Cambió deploy/keycloak/: ejecuta bash deploy/keycloak/ensure-realm.sh para dejar el realm al día.")
    # El sitio del servidor ya tiene lo que agregó certbot (certificado y redirección): no se copia encima.
    ! changed_since_pull deploy/nginx ||
      NOTES+=("Cambió deploy/nginx/ (git diff ${FROM_COMMIT:0:7} HEAD -- deploy/nginx): aplica el cambio a mano en $SITE, sin copiar el archivo encima porque perdería lo que agregó certbot, y recarga con sudo nginx -t && sudo systemctl reload nginx.")
  fi
  local missing
  missing="$(LC_ALL=C comm -13 <(env_names "$ENV_FILE") <(env_names "$ROOT/deploy/.env.example") | tr '\n' ' ')"
  missing="${missing% }"
  [ -z "$missing" ] ||
    NOTES+=("Estas variables de deploy/.env.example no están en $ENV_FILE (usan su valor por omisión; agrégalas si necesitas otro): $missing")
}

update() {
  echo "Brocal · actualizar $PUBLIC (imágenes :$TAG)"
  check_resources
  if $PULL; then
    pull_code
  else
    echo "  · código del servidor: $(git -C "$ROOT" log -1 --format='%h %s' 2>/dev/null || echo 'sin git')"
  fi

  local svc fp
  declare -A before_fp=()
  for svc in "${SERVICES[@]}"; do
    BEFORE[$svc]="$(current_image "$svc")"
    before_fp[$svc]="$(fingerprint "${BEFORE[$svc]}")"
    [ -z "${BEFORE[$svc]}" ] || protect "$svc" "${BEFORE[$svc]}"
  done
  build_images
  for svc in "${SERVICES[@]}"; do
    fp="$(fingerprint "${AFTER[$svc]}")"
    if [ -n "${before_fp[$svc]}" ] && [ "$fp" = "${before_fp[$svc]}" ]; then
      echo "  · $svc: sin cambios"
      # La etiqueta vuelve a la imagen en uso; la recompilación idéntica (otro ID con containerd) queda sin etiqueta.
      docker tag "${BEFORE[$svc]}" "${IMAGE[$svc]}"
      release "$svc"
    else
      echo "  · $svc: imagen nueva $(short "${AFTER[$svc]}") (antes: $(short "${BEFORE[$svc]}"))"
      CHANGED[$svc]=1
    fi
  done

  # La API aplica sus migraciones al arrancar: con una imagen nueva, primero el respaldo.
  [ -z "${CHANGED[brocal-api]:-}" ] || backup_database
  for svc in "${!CHANGED[@]}"; do keep_previous "$svc"; done
  start_and_check
  collect_notes
  rm -rf "$WORK"

  echo "Listo en $((SECONDS / 60)) min $((SECONDS % 60)) s: $PUBLIC"
  [ "$KEPT" -eq 0 ] || echo "  · para volver a la versión anterior: bash deploy/update.sh --revertir"
  local note
  for note in "${NOTES[@]}"; do echo "Aviso: $note"; done
}

# Intercambia cada imagen con su «anterior» (repetirlo regresa a la nueva). La base no se toca.
revert_images() {
  echo "Brocal · volver a la versión anterior de las imágenes (:$TAG)"
  local svc repo prev cur swapped=0 files
  for svc in "${SERVICES[@]}"; do
    repo="${IMAGE[$svc]%:*}"
    prev="$(image_id "$repo:anterior")"
    cur="$(current_image "$svc")"
    if [ -z "$prev" ]; then
      echo "  · $svc: no hay versión anterior"
      continue
    fi
    if [ "$(fingerprint "$prev")" = "$(fingerprint "$cur")" ]; then
      echo "  · $svc: ya usa la versión anterior"
      continue
    fi
    # La imagen en uso queda protegida mientras cambian las etiquetas (ver protect).
    [ -z "$cur" ] || protect "$svc" "$cur"
    docker tag "$prev" "${IMAGE[$svc]}"
    if [ -n "$cur" ]; then
      docker tag "$cur" "$repo:anterior"
      release "$svc"
    fi
    echo "  · $svc: $(short "$cur") → $(short "$prev")"
    swapped=$((swapped + 1))
  done
  [ "$swapped" -gt 0 ] || die "No hay versiones anteriores que restaurar."
  start_and_check
  rm -rf "$WORK"

  echo "Listo: $PUBLIC usa ahora las imágenes que estaban como «anterior», y las reemplazadas quedaron en su lugar (otro --revertir las regresa)."
  echo "La base no se tocó: si la versión nueva aplicó migraciones que la anterior no admite, restaura el respaldo previo a la actualización (docs/produccion.md, Respaldos)."
  shopt -s nullglob
  files=("$BACKUP_DIR"/brocal-*.dump)
  shopt -u nullglob
  [ "${#files[@]}" -eq 0 ] || echo "  · respaldo más reciente: ${files[-1]}"
}

main() {
  local arg
  for arg in "$@"; do
    case "$arg" in
      --sin-pull) PULL=false ;;
      --revertir) REVERT=true ;;
      -h | --ayuda) usage; return 0 ;;
      *) die "Opción desconocida: $arg (ver bash deploy/update.sh --ayuda)." ;;
    esac
  done
  preflight
  if $REVERT; then revert_images; else update; fi
}

# En una sola línea: bash ya leyó todo el script antes de ejecutarlo, así que un git pull que lo reemplace no
# afecta a esta ejecución.
main "$@"; exit
