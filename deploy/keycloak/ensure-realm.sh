#!/usr/bin/env bash
# Crea o actualiza el realm de producción de Brocal en el Keycloak de ChronoLith (idempotente):
#   · rol brocal-platform-admin (administradores de la plataforma Brocal)
#   · cliente brocal-web: SPA con PKCE para BROCAL_PUBLIC_URL
#   · cliente brocal-api: cuenta de servicio con KC_BROCAL_API_CLIENT_SECRET y manage-users (cuentas al invitar)
#   · correo del realm con los SMTP_* de deploy/.env ("¿Olvidaste tu contraseña?")
#   · opcional: el primer administrador de la plataforma, con una contraseña temporal
# Sin los usuarios de demostración ni el cliente brocal-dev-cli del modo de desarrollo.
#
#   bash deploy/keycloak/ensure-realm.sh [correo-del-administrador]
#
# Lee deploy/.env y pide la contraseña del administrador del realm master de Keycloak (o KC_ADMIN_USER y
# KC_ADMIN_PASSWORD). Detecta el contenedor de Keycloak de ChronoLith; otro: KEYCLOAK_CONTAINER=<nombre>.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ENV_FILE="${BROCAL_ENV_FILE:-$ROOT/deploy/.env}"
[ -f "$ENV_FILE" ] || { echo "Falta $ENV_FILE: cópialo de deploy/.env.example y complétalo." >&2; exit 1; }

# Valor de una variable de deploy/.env, sin ejecutar el archivo.
env_value() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1 | sed -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'$/\1/"; }
# Texto seguro dentro de una cadena JSON.
json() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'; }

REALM="$(env_value KEYCLOAK_REALM)"; REALM="${REALM:-brocal}"
PUBLIC="$(env_value BROCAL_PUBLIC_URL)"; PUBLIC="${PUBLIC%/}"
SECRET="$(env_value KC_BROCAL_API_CLIENT_SECRET)"
ADMIN_EMAIL="$(printf '%s' "${1:-$(env_value BROCAL_ADMIN_EMAIL)}" | tr '[:upper:]' '[:lower:]')"

case "$PUBLIC" in https://*) ;; *) echo "BROCAL_PUBLIC_URL debe ser https://… en $ENV_FILE" >&2; exit 1 ;; esac
case "$SECRET" in "" | genera-*) echo "Define KC_BROCAL_API_CLIENT_SECRET en $ENV_FILE (openssl rand -hex 32)" >&2; exit 1 ;; esac

CONTAINER="${KEYCLOAK_CONTAINER:-$(docker ps -q --filter label=com.docker.compose.project=chronolith-infra --filter label=com.docker.compose.service=keycloak | head -n 1)}"
[ -n "$CONTAINER" ] || { echo "No encontré el Keycloak de ChronoLith en ejecución; indica KEYCLOAK_CONTAINER=<nombre>." >&2; exit 1; }

export KC_ADMIN_USER="${KC_ADMIN_USER:-admin}"
if [ -z "${KC_ADMIN_PASSWORD:-}" ]; then
  read -rsp "Contraseña de $KC_ADMIN_USER (realm master de Keycloak): " KC_ADMIN_PASSWORD
  echo
fi
export KC_ADMIN_PASSWORD

kcadm() { docker exec "$CONTAINER" /opt/keycloak/bin/kcadm.sh "$@"; }
# Igual, con el JSON por la entrada estándar.
kcadm_json() { docker exec -i "$CONTAINER" /opt/keycloak/bin/kcadm.sh "$@" -f -; }
client_uuid() { kcadm get clients -r "$REALM" -q "clientId=$1" --fields id --format csv --noquotes | head -n 1; }

# Las credenciales se expanden dentro del contenedor: no quedan en la línea de comandos del servidor.
docker exec -e KC_ADMIN_USER -e KC_ADMIN_PASSWORD -e KC_ADMIN_SERVER "$CONTAINER" bash -c \
  '/opt/keycloak/bin/kcadm.sh config credentials --server "${KC_ADMIN_SERVER:-http://localhost:8080/auth}" --realm master --user "$KC_ADMIN_USER" --password "$KC_ADMIN_PASSWORD"' >/dev/null

echo "Keycloak $CONTAINER · realm $REALM · $PUBLIC"

if kcadm get "realms/$REALM" --fields realm >/dev/null 2>&1; then
  echo "  · el realm $REALM ya existe"
else
  kcadm_json create realms >/dev/null <<EOF
{
  "realm": "$REALM",
  "displayName": "Brocal",
  "displayNameHtml": "<span class=\"cl-realm-name\">Brocal</span>",
  "enabled": true,
  "sslRequired": "external",
  "registrationAllowed": false,
  "loginWithEmailAllowed": true,
  "duplicateEmailsAllowed": false,
  "resetPasswordAllowed": true,
  "rememberMe": true,
  "verifyEmail": false,
  "editUsernameAllowed": false,
  "bruteForceProtected": true,
  "permanentLockout": false,
  "failureFactor": 5,
  "waitIncrementSeconds": 60,
  "maxFailureWaitSeconds": 900,
  "accessTokenLifespan": 300,
  "ssoSessionIdleTimeout": 3600,
  "ssoSessionMaxLifespan": 43200,
  "offlineSessionIdleTimeout": 2592000,
  "passwordPolicy": "length(8) and upperCase(1) and lowerCase(1) and digits(1) and notUsername",
  "loginTheme": "chronolith",
  "internationalizationEnabled": true,
  "supportedLocales": ["es", "en"],
  "defaultLocale": "es",
  "roles": {
    "realm": [
      { "name": "brocal-platform-admin", "description": "Administrador de la plataforma Brocal: alta de empresas, cuotas, módulos y marca." }
    ]
  },
  "clients": [
    {
      "clientId": "brocal-web",
      "name": "Brocal (SPA)",
      "description": "Aplicación Angular de Brocal. OAuth 2.0 Authorization Code + PKCE (cliente público).",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": true,
      "standardFlowEnabled": true,
      "implicitFlowEnabled": false,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": false,
      "frontchannelLogout": true,
      "redirectUris": ["$PUBLIC/*"],
      "webOrigins": ["$PUBLIC"],
      "attributes": { "pkce.code.challenge.method": "S256", "post.logout.redirect.uris": "$PUBLIC/*" },
      "protocolMappers": [
        {
          "name": "brocal-api-audience",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-mapper",
          "consentRequired": false,
          "config": {
            "included.custom.audience": "brocal-api",
            "id.token.claim": "false",
            "access.token.claim": "true",
            "introspection.token.claim": "true"
          }
        }
      ]
    },
    {
      "clientId": "brocal-api",
      "name": "Brocal API",
      "description": "Cuenta de servicio de la API: crea cuentas al invitar usuarios (manage-users).",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": false,
      "clientAuthenticatorType": "client-secret",
      "secret": "$(json "$SECRET")",
      "standardFlowEnabled": false,
      "implicitFlowEnabled": false,
      "directAccessGrantsEnabled": false,
      "serviceAccountsEnabled": true
    }
  ]
}
EOF
  echo "  · realm $REALM creado (rol brocal-platform-admin, clientes brocal-web y brocal-api)"
fi

# Clientes: URL del sitio y secreto vigentes (también corrige un realm ya existente).
WEB_ID="$(client_uuid brocal-web)"
API_ID="$(client_uuid brocal-api)"
[ -n "$WEB_ID" ] && [ -n "$API_ID" ] || { echo "El realm $REALM no tiene los clientes brocal-web y brocal-api." >&2; exit 1; }
kcadm_json update "clients/$WEB_ID" -r "$REALM" <<EOF
{
  "redirectUris": ["$PUBLIC/*"],
  "webOrigins": ["$PUBLIC"],
  "attributes": { "pkce.code.challenge.method": "S256", "post.logout.redirect.uris": "$PUBLIC/*" }
}
EOF
kcadm_json update "clients/$API_ID" -r "$REALM" <<EOF
{ "secret": "$(json "$SECRET")", "serviceAccountsEnabled": true }
EOF
kcadm add-roles -r "$REALM" --uusername service-account-brocal-api --cclientid realm-management \
  --rolename manage-users --rolename view-users --rolename query-users
echo "  · brocal-web acepta $PUBLIC; brocal-api con su secreto y manage-users, view-users y query-users"

# Correo del realm ("¿Olvidaste tu contraseña?"), el mismo SMTP de la plataforma.
SMTP_HOST="$(env_value SMTP_HOST)"
if [ -n "$SMTP_HOST" ]; then
  SMTP_PORT="$(env_value SMTP_PORT)"; SMTP_PORT="${SMTP_PORT:-587}"
  SMTP_USER="$(env_value SMTP_USER)"
  SMTP_PASSWORD="$(env_value SMTP_PASSWORD)"
  SMTP_FROM="$(env_value SMTP_FROM)"; SMTP_FROM="${SMTP_FROM:-$SMTP_USER}"
  SMTP_FROM_NAME="$(env_value SMTP_FROM_NAME)"; SMTP_FROM_NAME="${SMTP_FROM_NAME:-Brocal}"
  if [ "$SMTP_PORT" = "465" ]; then SSL=true; STARTTLS=false; else SSL=false; STARTTLS=true; fi
  if [ -n "$SMTP_USER" ]; then AUTH=true; else AUTH=false; fi
  kcadm_json update "realms/$REALM" <<EOF
{
  "smtpServer": {
    "host": "$(json "$SMTP_HOST")",
    "port": "$(json "$SMTP_PORT")",
    "from": "$(json "$SMTP_FROM")",
    "fromDisplayName": "$(json "$SMTP_FROM_NAME")",
    "auth": "$AUTH",
    "user": "$(json "$SMTP_USER")",
    "password": "$(json "$SMTP_PASSWORD")",
    "ssl": "$SSL",
    "starttls": "$STARTTLS"
  }
}
EOF
  echo "  · correo del realm: $SMTP_HOST:$SMTP_PORT, remitente $SMTP_FROM"
else
  echo "  · sin SMTP_HOST: el realm no envía correos (\"¿Olvidaste tu contraseña?\" no funcionará)"
fi

# Primer administrador de la plataforma.
if [ -n "$ADMIN_EMAIL" ]; then
  find_admin() { kcadm get users -r "$REALM" -q "email=$ADMIN_EMAIL" -q exact=true --fields id --format csv --noquotes | head -n 1; }
  ADMIN_ID="$(find_admin)"
  if [ -n "$ADMIN_ID" ]; then
    echo "  · la cuenta $ADMIN_EMAIL ya existe"
  else
    read -rp "Nombre de $ADMIN_EMAIL [Administrador]: " FIRST_NAME
    read -rp "Apellidos [Brocal]: " LAST_NAME
    read -rsp "Contraseña temporal (8+ caracteres con mayúsculas, minúsculas y dígitos; se cambia al entrar): " TEMP_PASSWORD
    echo
    kcadm_json create users -r "$REALM" >/dev/null <<EOF
{
  "username": "$(json "$ADMIN_EMAIL")",
  "email": "$(json "$ADMIN_EMAIL")",
  "firstName": "$(json "${FIRST_NAME:-Administrador}")",
  "lastName": "$(json "${LAST_NAME:-Brocal}")",
  "enabled": true,
  "emailVerified": true,
  "credentials": [{ "type": "password", "value": "$(json "$TEMP_PASSWORD")", "temporary": true }]
}
EOF
    ADMIN_ID="$(find_admin)"
    echo "  · cuenta $ADMIN_EMAIL creada; pedirá una contraseña nueva al entrar"
  fi
  kcadm add-roles -r "$REALM" --uid "$ADMIN_ID" --rolename brocal-platform-admin
  echo "  · $ADMIN_EMAIL es administrador de la plataforma (brocal-platform-admin)"
else
  echo "  · sin administrador: créalo con  bash deploy/keycloak/ensure-realm.sh correo@dominio"
fi

echo "Listo: $PUBLIC"
