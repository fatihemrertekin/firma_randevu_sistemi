$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path deploy/.env)) {
    throw 'Önce yerel kurulum ve migration tamamlanmalı.'
}

$recoveryInstance = Read-Host 'Beklenen firma kimliği (yerelde firma-randevu-local)'
$recoveryOwnerInput = Read-Host 'Doğrulanmış Owner hesap UUID kimliği'
$recoveryOwnerId = [guid]::Empty
if (-not [guid]::TryParse($recoveryOwnerInput, [ref]$recoveryOwnerId) -or $recoveryOwnerId -eq [guid]::Empty) {
    throw 'Geçerli Owner UUID kimliği gerekli.'
}
$recoveryOperator = Read-Host 'Yetkili operatör referansı (kişisel veri kullanmayın)'
$recoveryRequest = Read-Host 'Benzersiz onay/destek kaydı referansı (kişisel veri kullanmayın)'
$recoveryTarget = "$recoveryInstance/$recoveryOwnerId"
$recoveryConfirmation = Read-Host "Eski MFA ve oturumlar iptal edilecek. Owner kimliği ve yetkisi doğrulandıysa aynen yazın: $recoveryTarget"
if ($recoveryConfirmation -cne $recoveryTarget) {
    throw 'Kurtarma onaylanmadı; değişiklik yapılmadı.'
}

$recoveryVariables = @{
    APP_RECOVERY_INSTANCE_ID = $recoveryInstance
    APP_RECOVERY_OWNER_ID = $recoveryOwnerId.ToString()
    APP_RECOVERY_OPERATOR_REF = $recoveryOperator
    APP_RECOVERY_REQUEST_REF = $recoveryRequest
    APP_RECOVERY_CONFIRM = $recoveryConfirmation
}
$recoveryPreviousValues = @{}
try {
    foreach ($recoveryName in $recoveryVariables.Keys) {
        $recoveryPreviousValues[$recoveryName] = [Environment]::GetEnvironmentVariable($recoveryName, 'Process')
        [Environment]::SetEnvironmentVariable($recoveryName, $recoveryVariables[$recoveryName], 'Process')
    }
    docker compose --env-file deploy/.env -f deploy/compose.local.yaml run --rm --no-deps `
        -e APP_RECOVERY_INSTANCE_ID -e APP_RECOVERY_OWNER_ID -e APP_RECOVERY_OPERATOR_REF `
        -e APP_RECOVERY_REQUEST_REF -e APP_RECOVERY_CONFIRM app recover-owner-mfa
    if ($LASTEXITCODE -ne 0) {
        throw 'Owner MFA kurtarma tamamlanmadı.'
    }
}
finally {
    foreach ($recoveryName in $recoveryPreviousValues.Keys) {
        [Environment]::SetEnvironmentVariable($recoveryName, $recoveryPreviousValues[$recoveryName], 'Process')
    }
}
