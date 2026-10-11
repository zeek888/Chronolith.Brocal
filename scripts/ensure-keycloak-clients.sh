#!/usr/bin/env bash
# Crea o actualiza en el realm de ChronoLith los clientes de Brocal (idempotente). Equivalente a
# ensure-keycloak-clients.ps1; requiere curl y jq.
#   chronolith-brocal-api   cuenta de servicio con el rol chronolith-module-registrar (registro en el Shell)
#   chronolith-brocal-web   cliente público con PKCE de la página independiente
#   brocal-platform-admin   rol de realm de los administradores de la plataforma Brocal
# Variables: KEYCLOAK_PUBLIC_URL, KEYCLOAK_REALM, KEYCLOAK_ADMIN, KEYCLOAK_ADMIN_PASSWORD,
#            KC_BROCAL_API_CLIENT_SECRET, BROCAL_PUBLIC_URL,
#            BROCAL_PROVISION_USERS=true (manage-users, view-users y query-users para crear cuentas al invitar)
set -euo pipefail

command -v jq >/dev/null || { echo "Se requiere jq (apt install jq)" >&2; exit 1; }

KC="${KEYCLOAK_PUBLIC_URL:-http://localhost:8080}"; KC="${KC%/}"
REALM="${KEYCLOAK_REALM:-chronolith}"
ADMIN="${KEYCLOAK_ADMIN:-admin}"
ADMIN_PASSWORD="${KEYCLOAK_ADMIN_PASSWORD:-admin}"
SECRET="${KC_BROCAL_API_CLIENT_SECRET:-chronolith-brocal-api-dev-secret}"
PUBLIC="${BROCAL_PUBLIC_URL:-http://localhost:4303}"; PUBLIC="${PUBLIC%/}"
PROVISION="${BROCAL_PROVISION_USERS:-false}"
BASE="$KC/admin/realms/$REALM"

echo "Keycloak $KC · realm $REALM"
TOKEN=$(curl -fsS -X POST "$KC/realms/master/protocol/openid-connect/token" \
  -d grant_type=password -d client_id=admin-cli --data-urlencode "username=$ADMIN" --data-urlencode "password=$ADMIN_PASSWORD" | jq -r .access_token)

api() { # método ruta [json]
  if [ $# -ge 3 ]; then
    curl -fsS -X "$1" "$BASE$2" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "$3"
  else
    curl -fsS -X "$1" "$BASE$2" -H "Authorization: Bearer $TOKEN"
  fi
}

client_id() { api GET "/clients?clientId=$1" | jq -r '.[0].id // empty'; }

AUDIENCE='{"name":"chronolith-api-audience","protocol":"openid-connect","protocolMapper":"oidc-audience-mapper","consentRequired":false,
  "config":{"included.custom.audience":"chronolith-api","id.token.claim":"false","access.token.claim":"true","introspection.token.claim":"true"}}'

save_client() { # clientId json
  local id; id=$(client_id "$1")
  if [ -n "$id" ]; then
    api PUT "/clients/$id" "$2" >/dev/null; echo "  · cliente $1 actualizado"
  else
    api POST /clients "$2" >/dev/null; id=$(client_id "$1"); echo "  · cliente $1 creado"
  fi
  if ! api GET "/clients/$id/protocol-mappers/models" | jq -e '.[] | select(.name == "chronolith-api-audience")' >/dev/null; then
    api POST "/clients/$id/protocol-mappers/models" "$AUDIENCE" >/dev/null; echo "    audiencia chronolith-api agregada"
  fi
  SAVED_ID="$id"
}

# Rol de administradores de la plataforma Brocal
if api GET /roles/brocal-platform-admin >/dev/null 2>&1; then
  echo "  · rol brocal-platform-admin ya existe"
else
  api POST /roles '{"name":"brocal-platform-admin","description":"Administrador de la plataforma Brocal: empresas, módulos contratados, cuotas, marca y suspensión."}' >/dev/null
  echo "  · rol brocal-platform-admin creado"
fi

# API: cuenta de servicio que registra el módulo en el Shell
save_client chronolith-brocal-api "$(jq -n --arg secret "$SECRET" '{
  clientId: "chronolith-brocal-api", name: "ChronoLith Brocal API",
  description: "Cuenta de servicio de Brocal para auto-registrar su manifiesto en el Shell.",
  enabled: true, protocol: "openid-connect", publicClient: false, clientAuthenticatorType: "client-secret", secret: $secret,
  standardFlowEnabled: false, implicitFlowEnabled: false, directAccessGrantsEnabled: false, serviceAccountsEnabled: true }')"
SA=$(api GET "/clients/$SAVED_ID/service-account-user" | jq -r .id)
if ! api GET "/users/$SA/role-mappings/realm" | jq -e '.[] | select(.name == "chronolith-module-registrar")' >/dev/null; then
  ROLE=$(api GET /roles/chronolith-module-registrar | jq '[{id: .id, name: .name}]')
  api POST "/users/$SA/role-mappings/realm" "$ROLE" >/dev/null
  echo "    rol chronolith-module-registrar asignado a la cuenta de servicio"
fi

if [ "$PROVISION" = "true" ]; then
  RM=$(client_id realm-management)
  CURRENT=$(api GET "/users/$SA/role-mappings/clients/$RM" | jq '[.[].name]')
  MISSING=$(api GET "/clients/$RM/roles" | jq --argjson current "$CURRENT" \
    '[.[] | select((.name == "manage-users" or .name == "view-users" or .name == "query-users") and (.name as $n | $current | index($n) | not)) | {id, name}]')
  if [ "$(echo "$MISSING" | jq length)" -gt 0 ]; then
    api POST "/users/$SA/role-mappings/clients/$RM" "$MISSING" >/dev/null
    echo "    roles de realm-management asignados: $(echo "$MISSING" | jq -r '[.[].name] | join(", ")')"
  else
    echo "    roles de realm-management ya asignados"
  fi
fi

# Página independiente de Brocal (SPA, PKCE)
ORIGINS=$(jq -n --arg public "$PUBLIC" '["http://localhost:4303", $public] | unique')
save_client chronolith-brocal-web "$(jq -n --argjson origins "$ORIGINS" '{
  clientId: "chronolith-brocal-web", name: "ChronoLith Brocal (página independiente)",
  description: "SPA de Brocal fuera del Shell. OAuth 2.0 Authorization Code + PKCE (cliente público).",
  enabled: true, protocol: "openid-connect", publicClient: true, standardFlowEnabled: true, implicitFlowEnabled: false,
  directAccessGrantsEnabled: false, serviceAccountsEnabled: false, frontchannelLogout: true,
  redirectUris: [$origins[] | . + "/*"], webOrigins: $origins,
  attributes: { "pkce.code.challenge.method": "S256", "post.logout.redirect.uris": ([$origins[] | . + "/*"] | join("##")) } }')"

echo "Listo: clientes de Brocal configurados."
