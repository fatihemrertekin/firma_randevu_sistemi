$ErrorActionPreference = 'Stop'

$envFile = Join-Path $PSScriptRoot '.env'
if (Test-Path -LiteralPath $envFile) {
    Write-Output 'deploy/.env zaten var; değiştirilmedi.'
    exit 0
}

$postgresBytes = New-Object byte[] 32
$appBytes = New-Object byte[] 32
$random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($postgresBytes)
    $random.GetBytes($appBytes)
} finally {
    $random.Dispose()
}
$postgresPassword = ([BitConverter]::ToString($postgresBytes)).Replace('-', '')
$appPassword = ([BitConverter]::ToString($appBytes)).Replace('-', '')
$lines = @(
    "POSTGRES_PASSWORD=$postgresPassword",
    "APP_DB_PASSWORD=$appPassword",
    'DB_PORT=55432'
)

[System.IO.File]::WriteAllLines($envFile, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output 'Yerel deploy/.env oluşturuldu. Sırlar ekrana yazdırılmadı.'
