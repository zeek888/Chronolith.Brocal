#!/usr/bin/env bash
# Prepara el entorno de desarrollo de Brocal (equivalente a dev-up.ps1).
#   Como módulo de ChronoLith:   ./scripts/dev-up.sh
#     1. PostgreSQL de Brocal (brocal-db, puerto 5436)
#     2. clientes de Keycloak si responde ChronoLith/infra
#     3. comandos para la API (http://localhost:5303) y el frontend (http://localhost:4303)
#   Independiente:               ./scripts/dev-up.sh --standalone
#     Keycloak propio (http://localhost:8181, realm brocal) y PostgreSQL; la API con el perfil standalone.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
KC="${KEYCLOAK_PUBLIC_URL:-http://localhost:8080}"

if [ "${1:-}" = "--standalone" ]; then
  echo "1/2 Keycloak (realm brocal) y PostgreSQL de Brocal..."
  docker compose -f "$ROOT/docker-compose.standalone.yml" up -d brocal-keycloak brocal-db
  PROFILE=standalone
else
  echo "1/3 PostgreSQL de Brocal (brocal-db)..."
  docker compose -f "$ROOT/docker-compose.yml" up -d brocal-db

  echo "2/3 Clientes de Keycloak..."
  if curl -fsS --max-time 5 "$KC/realms/chronolith/.well-known/openid-configuration" >/dev/null; then
    KEYCLOAK_PUBLIC_URL="$KC" "$ROOT/scripts/ensure-keycloak-clients.sh"
  else
    echo "Keycloak no responde en $KC; levanta ChronoLith/infra y ejecuta scripts/ensure-keycloak-clients.sh" >&2
  fi
  PROFILE=http
fi

echo "API y frontend:"
echo "  API:  dotnet run --project \"$ROOT/backend/src/ChronoLith.Brocal.Api\" --launch-profile $PROFILE"
echo "  Web:  npm --prefix \"$ROOT/frontend\" start"
if [ "$PROFILE" = "standalone" ]; then
  echo "Entra a http://localhost:4303 con admin / Brocal2026! (empresa demo: oficina, operador, direccion, cliente / Demo2026!)."
else
  echo "En el Shell (http://localhost:8081) Brocal aparece en el menú en cuanto la API registra su manifiesto."
fi
