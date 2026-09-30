param(
    [string]$ComposeFile = 'deploy/compose.local.yaml',
    [string]$EnvFile = 'deploy/.env'
)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path -LiteralPath $EnvFile) -or -not (Test-Path -LiteralPath $ComposeFile)) { throw 'Önce yerel kurulum ve migration tamamlanmalı.' }

$resetInstance = Read-Host 'Beklenen firma kimliği (yerelde firma-randevu-local)'
$resetOwnerInput = Read-Host 'Bağımsız destek kaydıyla doğrulanmış Owner UUID kimliği'
$resetOwnerId = [guid]::Empty
if (-not [guid]::TryParse($resetOwnerInput, [ref]$resetOwnerId) -or $resetOwnerId -eq [guid]::Empty) {
    throw 'Geçerli Owner UUID kimliği gerekli.'
}
$resetOperator = Read-Host 'Yetkili operatör referansı (kişisel veri kullanmayın)'
$resetRequest = Read-Host 'Benzersiz destek/onay referansı (kişisel veri kullanmayın)'
$resetTarget = "$resetInstance/$resetOwnerId"
$resetConfirmation = Read-Host "Sahiplik ve teslim kanalı doğrulandıysa aynen yazın: $resetTarget"
if ($resetConfirmation -cne $resetTarget) { throw 'Token üretimi onaylanmadı.' }

# Prepare access control before Docker can create the secret file. Never print its content.
$resetDirectory = Join-Path (Get-Location).Path ('.local/owner-password-reset/' + [guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($resetDirectory)
$resetAcl = New-Object System.Security.AccessControl.DirectorySecurity
$resetAcl.SetAccessRuleProtection($true, $false)
$resetIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
$resetRule = New-Object System.Security.AccessControl.FileSystemAccessRule($resetIdentity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$resetAcl.AddAccessRule($resetRule)
Set-Acl -LiteralPath $resetDirectory -AclObject $resetAcl

$resetVariables = @{
    APP_RESET_INSTANCE_ID = $resetInstance
    APP_RESET_OWNER_ID = $resetOwnerId.ToString()
    APP_RESET_OPERATOR_REF = $resetOperator
    APP_RESET_REQUEST_REF = $resetRequest
    APP_RESET_CONFIRM = $resetConfirmation
    APP_RESET_OUTPUT_PATH = '/reset-output/token.txt'
}
$resetPrevious = @{}
try {
    foreach ($resetName in $resetVariables.Keys) {
        $resetPrevious[$resetName] = [Environment]::GetEnvironmentVariable($resetName, 'Process')
        [Environment]::SetEnvironmentVariable($resetName, $resetVariables[$resetName], 'Process')
    }
    docker compose --env-file $EnvFile -f $ComposeFile run --rm --no-deps `
        --volume "${resetDirectory}:/reset-output" `
        -e APP_RESET_INSTANCE_ID -e APP_RESET_OWNER_ID -e APP_RESET_OPERATOR_REF `
        -e APP_RESET_REQUEST_REF -e APP_RESET_CONFIRM -e APP_RESET_OUTPUT_PATH app issue-owner-password-reset
    if ($LASTEXITCODE -ne 0) { throw 'Token teslimi doğrulanamadı. Dosyayı ve işlem kaydını kontrol edin; otomatik tekrar yapmayın.' }
    Write-Output ('Özel token dosyası: ' + (Join-Path $resetDirectory 'token.txt'))
    Write-Output 'Dosya içeriğini önceden doğrulanmış özel kanaldan Owner ile paylaşın; teslim/kullanım sonrası bu geçici dosyayı silin. Sohbete/loga eklemeyin.'
}
finally {
    foreach ($resetName in $resetPrevious.Keys) {
        [Environment]::SetEnvironmentVariable($resetName, $resetPrevious[$resetName], 'Process')
    }
}
