#requires -Version 7
param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
if (Test-Path (Join-Path $Directory 'ca.pem')) { throw 'Mevcut fixture sertifikaları korunuyor.' }
# Yalnız fixture dizinine yazar; işletim sistemi güven deposuna sertifika eklemez.
$rootKey = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synthetic fixture CA', $rootKey, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
$ca = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(8))
[IO.File]::WriteAllText((Join-Path $Directory 'ca.pem'), $ca.ExportCertificatePem())
foreach ($name in @('db-a', 'db-b', 'db-i', 'operator')) {
    $key = [Security.Cryptography.RSA]::Create(2048)
    $leafRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$name", $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $names.AddDnsName($name)
    if ($name -eq 'operator') { $names.AddDnsName('localhost'); $names.AddIpAddress([Net.IPAddress]::Loopback) }
    $leafRequest.CertificateExtensions.Add($names.Build())
    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($leafRequest.PublicKey, $false))
    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509AuthorityKeyIdentifierExtension]::CreateFromCertificate($ca, $true, $false))
    $usages = [Security.Cryptography.OidCollection]::new()
    [void]$usages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
    $leafRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $false))
    $leaf = $leafRequest.Create($ca, [DateTimeOffset]::UtcNow.AddMinutes(-3), [DateTimeOffset]::UtcNow.AddDays(7), [Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
    if ($name -eq 'operator') {
        $certificate = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($leaf, $key)
        [IO.File]::WriteAllBytes((Join-Path $Directory 'operator.pfx'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, [IO.File]::ReadAllText((Join-Path $Directory 'tls-password'))))
        $certificate.Dispose()
    } else {
        [IO.File]::WriteAllText((Join-Path $Directory "$name.crt"), $leaf.ExportCertificatePem())
        [IO.File]::WriteAllText((Join-Path $Directory "$name.key"), $key.ExportPkcs8PrivateKeyPem())
    }
    $leaf.Dispose(); $key.Dispose()
}
$ca.Dispose(); $rootKey.Dispose()
