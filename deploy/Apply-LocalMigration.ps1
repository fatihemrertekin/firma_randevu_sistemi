$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
$env:ASPNETCORE_ENVIRONMENT = 'Development'

$migrationSql = dotnet tool run dotnet-ef migrations script --idempotent --no-build --project src/Server --startup-project src/Server
if ($LASTEXITCODE -ne 0) {
    throw 'Migration SQL üretilemedi.'
}

$migrationSql | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw 'Migration uygulanamadı.'
}

Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app-user-grants.sql') -Raw | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -v ON_ERROR_STOP=1
if ($LASTEXITCODE -ne 0) {
    throw 'Uygulama rolü yetkileri verilemedi.'
}
