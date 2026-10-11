<#
.SYNOPSIS
  Crea o actualiza en el realm de ChronoLith los clientes de Brocal (idempotente).

.DESCRIPTION
  - chronolith-brocal-api: cuenta de servicio con el rol chronolith-module-registrar, para que la API publique su
    manifiesto en el Shell (PUT shell-api/api/modules/manifest) con la audiencia chronolith-api. Con
    -ProvisionUsers recibe además manage-users, view-users y query-users (realm-management) para crear la cuenta
    de quien se invita a una empresa (Identity__ProvisionUsers=true en la API).
  - chronolith-brocal-web: cliente público (Authorization Code + PKCE) de la página independiente de Brocal.
  - Rol de realm brocal-platform-admin: administradores de la plataforma Brocal (además de chronolith-admin y del
    permiso brocal.platform.manage del Shell).
  El realm de ChronoLith se importa solo la primera vez que arranca Keycloak; este script agrega los clientes de
  Brocal sin tocar el repositorio de ChronoLith. Requiere Keycloak en ejecución.

.EXAMPLE
  ./scripts/ensure-keycloak-clients.ps1
  ./scripts/ensure-keycloak-clients.ps1 -KeycloakUrl https://chronolith.com.mx/auth -AdminPassword '...' `
      -ApiClientSecret '...' -BrocalPublicUrl https://chronolith.com.mx/modules/brocal
#>
param(
  [string]$KeycloakUrl = $(if ($env:KEYCLOAK_PUBLIC_URL) { $env:KEYCLOAK_PUBLIC_URL } else { 'http://localhost:8080' }),
  [string]$Realm = $(if ($env:KEYCLOAK_REALM) { $env:KEYCLOAK_REALM } else { 'chronolith' }),
  [string]$AdminUser = $(if ($env:KEYCLOAK_ADMIN) { $env:KEYCLOAK_ADMIN } else { 'admin' }),
  [string]$AdminPassword = $(if ($env:KEYCLOAK_ADMIN_PASSWORD) { $env:KEYCLOAK_ADMIN_PASSWORD } else { 'admin' }),
  [string]$ApiClientSecret = $(if ($env:KC_BROCAL_API_CLIENT_SECRET) { $env:KC_BROCAL_API_CLIENT_SECRET } else { 'chronolith-brocal-api-dev-secret' }),
  [string]$BrocalPublicUrl = $(if ($env:BROCAL_PUBLIC_URL) { $env:BROCAL_PUBLIC_URL } else { 'http://localhost:4303' }),
  [switch]$ProvisionUsers
)

$ErrorActionPreference = 'Stop'
$KeycloakUrl = $KeycloakUrl.TrimEnd('/')
$BrocalPublicUrl = $BrocalPublicUrl.TrimEnd('/')

Write-Host "Keycloak $KeycloakUrl · realm $Realm"
$token = (Invoke-RestMethod -Method Post -Uri "$KeycloakUrl/realms/master/protocol/openid-connect/token" `
    -ContentType 'application/x-www-form-urlencoded' `
    -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = $AdminUser; password = $AdminPassword }).access_token
$headers = @{ Authorization = "Bearer $token" }
$base = "$KeycloakUrl/admin/realms/$Realm"

function Invoke-Admin([string]$Method, [string]$Path, $Body = $null) {
  $parameters = @{ Method = $Method; Uri = "$base$Path"; Headers = $headers }
  if ($null -ne $Body) {
    $parameters.ContentType = 'application/json; charset=utf-8'
    $parameters.Body = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 10))
  }
  # Windows PowerShell entrega un arreglo JSON como un solo objeto: se desenrolla para filtrar elemento por elemento.
  Invoke-RestMethod @parameters | ForEach-Object { $_ }
}

function Get-ClientId([string]$ClientId) {
  $found = @(Invoke-Admin Get "/clients?clientId=$ClientId")
  if ($found.Count -gt 0) { return $found[0].id }
  return $null
}

$audience = @{
  name = 'chronolith-api-audience'
  protocol = 'openid-connect'
  protocolMapper = 'oidc-audience-mapper'
  consentRequired = $false
  config = @{
    'included.custom.audience' = 'chronolith-api'
    'id.token.claim' = 'false'
    'access.token.claim' = 'true'
    'introspection.token.claim' = 'true'
  }
}

function Save-Client($Definition) {
  $id = Get-ClientId $Definition.clientId
  if ($id) {
    Invoke-Admin Put "/clients/$id" $Definition | Out-Null
    Write-Host "  · cliente $($Definition.clientId) actualizado"
  } else {
    Invoke-Admin Post '/clients' $Definition | Out-Null
    $id = Get-ClientId $Definition.clientId
    Write-Host "  · cliente $($Definition.clientId) creado"
  }
  # Los mapeadores no se actualizan con PUT: se agrega la audiencia si falta.
  $mappers = @(Invoke-Admin Get "/clients/$id/protocol-mappers/models")
  if (-not ($mappers | Where-Object { $_.name -eq $audience.name })) {
    Invoke-Admin Post "/clients/$id/protocol-mappers/models" $audience | Out-Null
    Write-Host '    audiencia chronolith-api agregada'
  }
  return $id
}

# ---- Rol de administradores de la plataforma Brocal ---------------------------------------------------------
try {
  Invoke-Admin Get '/roles/brocal-platform-admin' | Out-Null
  Write-Host '  · rol brocal-platform-admin ya existe'
} catch {
  Invoke-Admin Post '/roles' @{ name = 'brocal-platform-admin'; description = 'Administrador de la plataforma Brocal: empresas, módulos contratados, cuotas, marca y suspensión.' } | Out-Null
  Write-Host '  · rol brocal-platform-admin creado'
}

# ---- API: cuenta de servicio que registra el módulo en el Shell ---------------------------------------------
$apiId = Save-Client @{
  clientId = 'chronolith-brocal-api'
  name = 'ChronoLith Brocal API'
  description = 'Cuenta de servicio de Brocal para auto-registrar su manifiesto en el Shell.'
  enabled = $true
  protocol = 'openid-connect'
  publicClient = $false
  clientAuthenticatorType = 'client-secret'
  secret = $ApiClientSecret
  standardFlowEnabled = $false
  implicitFlowEnabled = $false
  directAccessGrantsEnabled = $false
  serviceAccountsEnabled = $true
}

$serviceAccount = Invoke-Admin Get "/clients/$apiId/service-account-user"
$registrar = Invoke-Admin Get '/roles/chronolith-module-registrar'
$assigned = @(Invoke-Admin Get "/users/$($serviceAccount.id)/role-mappings/realm")
if (-not ($assigned | Where-Object { $_.name -eq 'chronolith-module-registrar' })) {
  Invoke-Admin Post "/users/$($serviceAccount.id)/role-mappings/realm" @(@{ id = $registrar.id; name = $registrar.name }) | Out-Null
  Write-Host '    rol chronolith-module-registrar asignado a la cuenta de servicio'
}

if ($ProvisionUsers) {
  $realmManagement = Get-ClientId 'realm-management'
  $wanted = @('manage-users', 'view-users', 'query-users')
  $current = @(Invoke-Admin Get "/users/$($serviceAccount.id)/role-mappings/clients/$realmManagement") | ForEach-Object { $_.name }
  $missing = @(Invoke-Admin Get "/clients/$realmManagement/roles") |
    Where-Object { $wanted -contains $_.name -and $current -notcontains $_.name } |
    ForEach-Object { @{ id = $_.id; name = $_.name } }
  if ($missing.Count -gt 0) {
    Invoke-Admin Post "/users/$($serviceAccount.id)/role-mappings/clients/$realmManagement" @($missing) | Out-Null
    Write-Host "    roles de realm-management asignados: $(($missing | ForEach-Object { $_.name }) -join ', ')"
  } else {
    Write-Host '    roles de realm-management ya asignados'
  }
}

# ---- Página independiente de Brocal (SPA, PKCE) -------------------------------------------------------------
$origins = @('http://localhost:4303', $BrocalPublicUrl) | Select-Object -Unique
Save-Client @{
  clientId = 'chronolith-brocal-web'
  name = 'ChronoLith Brocal (página independiente)'
  description = 'SPA de Brocal fuera del Shell. OAuth 2.0 Authorization Code + PKCE (cliente público).'
  enabled = $true
  protocol = 'openid-connect'
  publicClient = $true
  standardFlowEnabled = $true
  implicitFlowEnabled = $false
  directAccessGrantsEnabled = $false
  serviceAccountsEnabled = $false
  frontchannelLogout = $true
  redirectUris = @($origins | ForEach-Object { "$_/*" })
  webOrigins = @($origins)
  attributes = @{
    'pkce.code.challenge.method' = 'S256'
    'post.logout.redirect.uris' = (($origins | ForEach-Object { "$_/*" }) -join '##')
  }
} | Out-Null

Write-Host 'Listo: clientes de Brocal configurados.'
