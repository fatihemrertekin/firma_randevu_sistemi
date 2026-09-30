$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path deploy/.env)) {
    throw 'Önce deploy/Initialize-LocalEnv.ps1 çalıştırılmalı.'
}

$ownerEmail = Read-Host 'Owner e-posta adresi'
$securePassword = Read-Host 'Owner parolası' -AsSecureString
$passwordBuffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $env:APP_BOOTSTRAP_OWNER_EMAIL = $ownerEmail
    $env:APP_BOOTSTRAP_OWNER_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordBuffer)
    docker compose --env-file deploy/.env -f deploy/compose.local.yaml run --rm --no-deps `
        -e APP_BOOTSTRAP_OWNER_EMAIL -e APP_BOOTSTRAP_OWNER_PASSWORD app bootstrap-owner
    if ($LASTEXITCODE -ne 0) {
        throw 'Owner hesabı oluşturulamadı.'
    }
}
finally {
    Remove-Item Env:APP_BOOTSTRAP_OWNER_EMAIL -ErrorAction SilentlyContinue
    Remove-Item Env:APP_BOOTSTRAP_OWNER_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordBuffer)
}
