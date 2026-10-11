# ---- Compilación ----
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run i18n:check && npx ng build --configuration production

# ---- Servidor web (nginx sin privilegios: escucha en 8080 con el usuario 101) ----
FROM nginxinc/nginx-unprivileged:1.29-alpine AS final

# La plantilla se procesa con envsubst al arrancar (solo las variables definidas: las de nginx se conservan).
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 docker/40-brocal-config.sh /docker-entrypoint.d/40-brocal-config.sh
COPY --from=build --chown=101:101 /app/dist/brocal/browser /usr/share/nginx/html

# Valores de desarrollo del modo módulo; docker compose los reemplaza por ambiente (la misma imagen sirve en todos).
ENV BROCAL_API_UPSTREAM=http://brocal-api:8080 \
    KEYCLOAK_URL=http://localhost:8080 \
    KEYCLOAK_REALM=chronolith \
    KEYCLOAK_CLIENT_ID=chronolith-brocal-web \
    CHRONOLITH_URL=http://localhost:8081

EXPOSE 8080
