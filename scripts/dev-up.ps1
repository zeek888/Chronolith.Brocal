<#
.SYNOPSIS
  Prepara el entorno de desarrollo de Brocal.

.DESCRIPTION
  Como módulo de ChronoLith (por omisión):
    1. Levanta PostgreSQL de Brocal (servicio brocal-db de docker-compose.yml, puerto 5436).
    2. Si Keycloak de ChronoLith responde, crea o actualiza los clientes de Brocal.
    3. Con -Start abre la API (http://localhost:5303, perfil http) y Angular (http://localhost:4303) en ventanas
       nuevas; sin él, muestra los comandos. ChronoLith/infra y ChronoLith/shell deben estar en ejecución.
  Independiente (-Standalone):
    1. Levanta Keycloak propio (http://localhost:8181, realm brocal) y PostgreSQL de docker-compose.standalone.yml.
    2. Con -Start abre la API (perfil standalone) y Angular. Usuario: admin / Brocal2026!.
  Requiere Docker, .NET 10 SDK y Node 22+.

.EXAMPLE
  ./scripts/dev-up.ps1 -Start
  ./scripts/dev-up.ps1 -Standalone -Start
#>
param(
  [switch]$Start,
  [switch]$Standalone,
  [string]$KeycloakUrl = 'http://localhost:8080'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ($Standalone) {
  Write-Host '1/2 Keycloak (realm brocal) y PostgreSQL de Brocal...'
  # Variables propias del modo independiente: el .env de la carpeta es del modo módulo.
  $envFile = Join-Path $root 'standalone.env'
  if (-not (Test-Path $envFile)) { $envFile = Join-Path $root '.env.standalone.example' }
  docker compose -f (Join-Path $root 'docker-compose.standalone.yml') --env-file $envFile up -d brocal-keycloak brocal-db
  if ($LASTEXITCODE -ne 0) { throw 'No se pudieron levantar brocal-keycloak y brocal-db.' }
  $launchProfile = 'standalone'
} else {
  Write-Host '1/3 PostgreSQL de Brocal (brocal-db)...'
  docker compose -f (Join-Path $root 'docker-compose.yml') up -d brocal-db
  if ($LASTEXITCODE -ne 0) { throw 'No se pudo levantar brocal-db.' }

  Write-Host '2/3 Clientes de Keycloak...'
  try {
    Invoke-RestMethod -Uri "$KeycloakUrl/realms/chronolith/.well-known/openid-configuration" -TimeoutSec 5 | Out-Null
    & (Join-Path $PSScriptRoot 'ensure-keycloak-clients.ps1') -KeycloakUrl $KeycloakUrl
  } catch {
    Write-Warning "Keycloak no responde en $KeycloakUrl; levanta ChronoLith/infra y vuelve a ejecutar scripts/ensure-keycloak-clients.ps1."
  }
  $launchProfile = 'http'
}

$api = "dotnet run --project `"$(Join-Path $root 'backend\src\ChronoLith.Brocal.Api')`" --launch-profile $launchProfile"
$web = "npm --prefix `"$(Join-Path $root 'frontend')`" start"

Write-Host 'API y frontend'
if ($Start) {
  Start-Process powershell -ArgumentList '-NoExit', '-Command', $api
  Start-Process powershell -ArgumentList '-NoExit', '-Command', $web
  Write-Host 'Abiertos en ventanas nuevas: API http://localhost:5303 (Scalar en /scalar) · web http://localhost:4303'
} else {
  Write-Host "  API:  $api"
  Write-Host "  Web:  $web"
}

if ($Standalone) {
  Write-Host 'Entra a http://localhost:4303 con admin / Brocal2026! (empresa demo: oficina, operador, direccion, cliente / Demo2026!).'
} else {
  Write-Host 'En el Shell (http://localhost:8081) Brocal aparece en el menú en cuanto la API registra su manifiesto.'
}
