#!/bin/sh
# Escribe la configuración de ejecución de la página independiente (Keycloak y enlace al Shell) a partir de
# variables de entorno, para promover la misma imagen entre ambientes. Dentro de ChronoLith no se usa: el Shell
# provee la sesión.
set -eu

escape() {
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

if [ -n "${CHRONOLITH_URL:-}" ]; then
  chronolith="\"$(escape "${CHRONOLITH_URL}")\""
else
  chronolith=null
fi

cat > /usr/share/nginx/html/config.json <<EOF
{
  "keycloak": {
    "url": "$(escape "${KEYCLOAK_URL}")",
    "realm": "$(escape "${KEYCLOAK_REALM}")",
    "clientId": "$(escape "${KEYCLOAK_CLIENT_ID}")"
  },
  "chronolithUrl": ${chronolith}
}
EOF

echo "brocal: config.json escrito (Keycloak ${KEYCLOAK_URL}, realm ${KEYCLOAK_REALM}, cliente ${KEYCLOAK_CLIENT_ID})"
