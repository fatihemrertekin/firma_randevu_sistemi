$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Bu kurulum adımı Windows içindir.' }
$taskPrivatePath = [IO.Path]::GetFullPath((Join-Path $PWD '.local/p02-14-private'))
if ([IO.Path]::GetDirectoryName($taskPrivatePath) -ne [IO.Path]::GetFullPath((Join-Path $PWD '.local'))) { throw 'Özel dizin hedefi geçersiz.' }
if (Test-Path -LiteralPath $taskPrivatePath) {
    if ((Get-Item -LiteralPath $taskPrivatePath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Özel dizin bağlantı olamaz.' }
}
New-Item -ItemType Directory -Path $taskPrivatePath -Force | Out-Null
$taskUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls.exe $taskPrivatePath /inheritance:r /grant:r "${taskUser}:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Özel dizin erişimi sınırlandırılamadı.' }
$taskEnvFile = Join-Path $taskPrivatePath 'smtp.env'
if (Test-Path -LiteralPath $taskEnvFile) { throw 'Mevcut özel ayar dosyası korunuyor; üzerine yazılmadı.' }
function Test-Address([string]$value) {
    try { $address = New-Object System.Net.Mail.MailAddress($value); return $address.Address -eq $value -and $value -notmatch '[\s\x00-\x1f\x7f]' }
    catch { return $false }
}
Write-Host 'Yalnız ayrı Gmail test hesabınızı kullanın. Ana işletme hesabınız bu teste alınmayacak.'
$taskSender = (Read-Host 'Gönderen Gmail test adresi').Trim()
if (!(Test-Address $taskSender)) { throw 'Gönderen adresi geçersiz.' }
$taskRecipient = (Read-Host 'Alıcı test adresi (aynı adres için Enter)').Trim()
if (!$taskRecipient) { $taskRecipient = $taskSender }
if (!(Test-Address $taskRecipient)) { throw 'Alıcı adresi geçersiz.' }
Write-Host 'Normal Gmail parolanızı girmeyin. İki aşamalı doğrulama açık test hesabının uygulama şifresini kullanın.'
$taskSecurePassword = Read-Host 'Gmail uygulama şifresi (gizli giriş)' -AsSecureString
$taskPasswordPointer = [IntPtr]::Zero
try {
    $taskPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($taskSecurePassword)
    $taskPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($taskPasswordPointer) -replace '\s', ''
    if ($taskPassword -notmatch '^[A-Za-z0-9]{16}$') { throw '16 karakterlik uygulama şifresi gerekli.' }
    $taskContent = "SMTP_USERNAME=$taskSender`nSMTP_FROM_ADDRESS=$taskSender`nSMTP_TEST_RECIPIENT=$taskRecipient`nSMTP_PASSWORD=$taskPassword`n"
    # Parent ACL is private before the first secret is written. Never print the content.
    $taskStream = [IO.File]::Open($taskEnvFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $taskBytes = [Text.UTF8Encoding]::new($false).GetBytes($taskContent)
        $taskStream.Write($taskBytes, 0, $taskBytes.Length)
    } finally { $taskStream.Dispose(); if ($taskBytes) { [Array]::Clear($taskBytes, 0, $taskBytes.Length) } }
} finally {
    if ($taskPasswordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($taskPasswordPointer) }
    $taskPassword = $null; $taskContent = $null; $taskSecurePassword.Dispose()
}
Write-Host 'Özel test ayarları kaydedildi. Bu komut gönderim veya hesap/parola değişikliği yapmadı.'
Write-Host 'Dosya içeriğini sohbete göndermeyin. Hazır olduğunu bildirmeniz yeterli.'
