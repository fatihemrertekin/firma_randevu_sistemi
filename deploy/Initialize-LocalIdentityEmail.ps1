$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Bu kurulum adımı Windows içindir.' }
$taskWorkspace = [IO.Path]::GetFullPath($PWD.Path)
$taskPrivatePath = [IO.Path]::GetFullPath((Join-Path $taskWorkspace '.local/identity-email-private'))
if ([IO.Path]::GetDirectoryName($taskPrivatePath) -ne (Join-Path $taskWorkspace '.local')) { throw 'Özel dizin hedefi geçersiz.' }
foreach ($path in @((Join-Path $taskWorkspace '.local'), $taskPrivatePath)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Özel dizin yolu bağlantı olamaz.'
    }
}
New-Item -ItemType Directory -Path $taskPrivatePath -Force | Out-Null
$taskUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $taskPrivatePath /inheritance:r /grant:r "${taskUser}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Özel dizin erişimi sınırlandırılamadı.' }
$taskEnvFile = Join-Path $taskPrivatePath 'app.env'
if (Test-Path -LiteralPath $taskEnvFile) { throw 'Mevcut özel ayarlar korunuyor; üzerine yazılmadı.' }

# Read only the registered Owner destination; never print it or change the account.
$taskOwnerSql = 'SELECT u."Email" FROM "AspNetUsers" u JOIN "AspNetUserRoles" ur ON ur."UserId"=u."Id" JOIN "AspNetRoles" r ON r."Id"=ur."RoleId" WHERE r."Name"=''Owner'';'
$taskOwnerRows = @($taskOwnerSql | docker compose --env-file deploy/.env -f deploy/compose.local.yaml exec -T db psql -U postgres -d firma_randevu -At -v ON_ERROR_STOP=1)
if ($LASTEXITCODE -ne 0 -or $taskOwnerRows.Count -ne 1) { throw 'Tek mevcut Owner adresi doğrulanamadı.' }
$taskRecipient = $taskOwnerRows[0].Trim()
if ($taskRecipient -notmatch '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$' -or $taskRecipient.EndsWith('.test')) {
    throw 'Mevcut Owner adresi gerçek posta teslimi için uygun değil; hesap değiştirilmedi.'
}
Write-Host 'Bu ayarlar ana yerel kurulumda kalıcı gönderim içindir; Owner hesabı/parolası/MFA değiştirilmez.'
$taskSender = (Read-Host 'Gönderen Gmail adresi').Trim()
if ($taskSender -notmatch '^[A-Za-z0-9._%+-]+@(gmail\.com|googlemail\.com)$') { throw 'Geçerli Gmail gönderen adresi gerekli.' }
Write-Host 'Normal Gmail parolanızı girmeyin. İki aşamalı doğrulaması açık hesabın uygulama şifresini kullanın.'
$taskSecurePassword = Read-Host 'Gmail uygulama şifresi (gizli giriş)' -AsSecureString
$taskPointer = [IntPtr]::Zero
try {
    $taskPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecurePassword)
    $taskPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPointer) -replace '\s', ''
    if ($taskPassword -notmatch '^[A-Za-z0-9]{16}$') { throw '16 karakterlik Google uygulama şifresi gerekli.' }
    $taskContent = @"
IdentityEmail__Mode=Smtp
IdentityEmail__PublicOrigin=http://localhost:8080
IdentityEmail__Smtp__Host=smtp.gmail.com
IdentityEmail__Smtp__Port=587
IdentityEmail__Smtp__Username=$taskSender
IdentityEmail__Smtp__Password=$taskPassword
IdentityEmail__Smtp__FromAddress=$taskSender
IdentityEmail__Smtp__DailyLimit=50
IdentityEmail__Smtp__AllowedRecipients__0=$taskRecipient
OwnerPasswordReset__Enabled=true
"@
    $taskStream = [IO.File]::Open($taskEnvFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $taskBytes = [Text.UTF8Encoding]::new($false).GetBytes($taskContent)
        $taskStream.Write($taskBytes, 0, $taskBytes.Length)
    } finally { $taskStream.Dispose(); if ($taskBytes) { [Array]::Clear($taskBytes, 0, $taskBytes.Length) } }
} finally {
    if ($taskPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPointer) }
    $taskSecurePassword.Dispose(); $taskPassword = $null; $taskContent = $null
}
Write-Host 'Kalıcı özel gönderim ayarları kaydedildi. İçeriği sohbete göndermeyin.'
Write-Host 'Bu komut ileti göndermedi veya uygulamayı yeniden başlatmadı. Hazır olduğunu bildirmeniz yeterli.'
