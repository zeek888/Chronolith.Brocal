# Brocal

**Documentación operativa para empresas de servicios petroleros**, multiempresa: boletas de servicio digitales
(captura en campo sin señal, firma del cliente, fotos y PDF con la marca de la empresa), órdenes de trabajo y
programación, tarifarios, flota y mantenimiento, permisos por unidad, combustible, inventario, expedientes del
personal (DC-3, H2S, SSPA), HSE, cuadrillas, viáticos, facturación CFDI 4.0, Carta Porte, cobranza con COPADE,
cotizaciones, dashboard gerencial, portal del cliente, reportes, bitácora, notificaciones (correo y WhatsApp),
integraciones (GPS, CONTPAQi/Aspel, Excel), flujos de autorización y reglas de negocio configurables por empresa
o para todas, y revisión automática de vencimientos.

Funciona **como módulo de ChronoLith** (se carga en el Shell bajo `/brocal` y toma su sesión, tema e idioma) o
**de forma independiente** con su propio Keycloak. Cada empresa tiene sus módulos contratados, cuotas, colores,
logotipos, configuración operativa y fiscal, usuarios y roles.

| Capa | Tecnología |
|---|---|
| Backend | .NET 10, Clean Architecture, minimal APIs, EF Core 10 + PostgreSQL 17 |
| Frontend | Angular 22, Angular Material 3, Native Federation (remote del Shell y aplicación propia) |
| Seguridad | Keycloak 26 (OIDC, Authorization Code + PKCE), permisos por empresa y rol |

## Inicio rápido (independiente, Docker)

```bash
cp .env.standalone.example standalone.env       # una vez; ajusta contraseñas y SMTP si hace falta
docker compose -f docker-compose.standalone.yml --env-file standalone.env up -d --build
```

| Servicio | URL | Usuario de desarrollo |
|---|---|---|
| Brocal | http://localhost:4303 | `admin` / `Brocal2026!` (plataforma y empresa demo) |
| Usuarios de la empresa demo | — | `oficina`, `supervisor`, `operador`, `direccion`, `cliente` / `Demo2026!` |
| Consola de Keycloak | http://localhost:8181/admin | `admin` / `admin` |

Las credenciales son solo para desarrollo; cámbialas en `standalone.env` para otros ambientes. El modo
independiente lleva su propio archivo porque el `.env` de la carpeta es del modo módulo (apunta al Keycloak de
ChronoLith) y Compose lo leería por omisión; git ignora ambos.

## Como módulo de ChronoLith

Con `ChronoLith/infra` y `ChronoLith/shell` en ejecución:

```powershell
./scripts/ensure-keycloak-clients.ps1   # una vez: clientes chronolith-brocal-api / -web
docker compose up -d --build            # Brocal se registra en el Shell y aparece en el menú
```

Detalles, producción detrás del gateway y modo independiente: [docs/integracion-chronolith.md](docs/integracion-chronolith.md).

## Desarrollo local

```powershell
./scripts/dev-up.ps1 -Start               # módulo: PostgreSQL + clientes de Keycloak + API y Angular
./scripts/dev-up.ps1 -Standalone -Start   # independiente: Keycloak propio + PostgreSQL + API y Angular
```

| Servicio | Puerto |
|---|---|
| Web (Angular, remote y página independiente) | 4303 |
| API (desarrollo; `/scalar` documenta la API) | 5303 |
| PostgreSQL | 5436 |
| Keycloak independiente | 8181 |

Pruebas y verificaciones:

```bash
cd backend  && dotnet test                     # dominio, arquitectura e integración (Testcontainers: requiere Docker)
cd frontend && npm run i18n:check && npx ng build
```

## Estructura

```
backend/    API .NET 10 (Domain, Application, Infrastructure, Api) y pruebas
frontend/   Angular 22: remote de Native Federation + aplicación independiente
infra/      realm "brocal" de Keycloak y tema de login de ChronoLith (modo independiente)
scripts/    clientes de Keycloak en el realm de ChronoLith y arranque de desarrollo
docs/       documentación
```

## Documentación

- [Arquitectura](docs/arquitectura.md): capas, multiempresa, seguridad, fiscal, tema y captura sin señal.
- [Módulos](docs/modulos.md): los 25 módulos, pantallas, permisos, roles predeterminados y API de integraciones.
- [Administración multiempresa](docs/administracion.md): alta de empresas, cuotas, marca, usuarios y roles.
- [Flujos, reglas y tareas programadas](docs/automatizacion.md): autorizaciones por pasos, reglas que bloquean o
  avisan (por empresa o globales) y revisión de vencimientos.
- [Integración con ChronoLith](docs/integracion-chronolith.md): contrato de módulo, desarrollo y producción.
- [Despliegue](docs/despliegue.md): imágenes, composiciones, configuración, respaldos y lista para producción.

## Pendientes conocidos

- **PAC**: incluye el PAC "Simulado" (CFDI 4.0 completo con timbre de prueba, sin validez fiscal). Para timbrar en
  producción hay que implementar el adaptador del PAC contratado (`IPacProvider`).
- WhatsApp requiere una cuenta de WhatsApp Business (Cloud API de Meta) con plantillas aprobadas.
