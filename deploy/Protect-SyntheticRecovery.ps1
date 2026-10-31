param(
    [Parameter(Mandatory)][ValidateSet('Encrypt','Decrypt')][string]$Operation,
    [Parameter(Mandatory)][string]$KeyFile
)
$ErrorActionPreference='Stop'
try {
    # stdin/stdout yalnız çağıran sürecin belleğidir; sır veya düz arşiv dosyası oluşturulmaz.
    $key=[IO.File]::ReadAllBytes($KeyFile)
    if ($key.Length -ne 32) { throw 'Key size' }
    $inputBytes=[Convert]::FromBase64String([Console]::In.ReadToEnd())
    if ($inputBytes.Length -gt 67108900) { throw 'Size limit' }
    $magic=[Text.Encoding]::ASCII.GetBytes('FRBK01')
    $aes=[Security.Cryptography.AesGcm]::new($key,16)
    try {
        if ($Operation -eq 'Encrypt') {
            $nonce=[Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
            $tag=[byte[]]::new(16);$cipher=[byte[]]::new($inputBytes.Length)
            $aes.Encrypt($nonce,$inputBytes,$cipher,$tag,$magic)
            [Console]::Out.Write([Convert]::ToBase64String([byte[]]($magic+$nonce+$tag+$cipher)))
        } else {
            if ($inputBytes.Length -lt 34 -or [Text.Encoding]::ASCII.GetString($inputBytes,0,6) -cne 'FRBK01') { throw 'Format' }
            $plain=[byte[]]::new($inputBytes.Length-34)
            $aes.Decrypt([byte[]]$inputBytes[6..17],[byte[]]$inputBytes[34..($inputBytes.Length-1)],
                [byte[]]$inputBytes[18..33],$plain,$magic)
            [Console]::Out.Write([Convert]::ToBase64String($plain))
        }
    } finally { $aes.Dispose();[Array]::Clear($key,0,$key.Length) }
} catch {
    [Console]::Error.WriteLine('Sentetik paket şifreleme/doğrulama başarısız; çıktı kullanılmadı.')
    exit 1
}
