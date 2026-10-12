<#
.SYNOPSIS
  Assina arquivos (Authenticode, SHA-256) com o certificado .pfx indicado em INSIGHTFLOW_SIGN_PFX.
.DESCRIPTION
  A senha do .pfx vem de INSIGHTFLOW_SIGN_PASSWORD, nunca da linha de comando. Usa Set-AuthenticodeSignature,
  que acompanha o PowerShell: nao exige o signtool do Windows SDK.
  Mensagens sem acentos: o Windows PowerShell 5.1 le scripts sem BOM como ANSI.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Path,
    # Vazio assina sem carimbo de tempo: a assinatura deixa de valer quando o certificado expira.
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
if (-not $env:INSIGHTFLOW_SIGN_PFX) { throw 'Defina INSIGHTFLOW_SIGN_PFX com o caminho do certificado (.pfx).' }

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    (Resolve-Path $env:INSIGHTFLOW_SIGN_PFX).Path, $env:INSIGHTFLOW_SIGN_PASSWORD)
try {
    foreach ($file in $Path) {
        $arguments = @{ FilePath = $file; Certificate = $certificate; HashAlgorithm = 'SHA256' }
        if ($TimestampServer) { $arguments.TimestampServer = $TimestampServer }
        $signature = Set-AuthenticodeSignature @arguments
        if (-not $signature.SignerCertificate -or $signature.Status -in 'NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible') {
            throw "Nao foi possivel assinar ${file}: $($signature.StatusMessage)"
        }
        if ($signature.Status -ne 'Valid') {
            # Certificado de teste ou de uma autoridade que este computador nao conhece: assinado, mas nao confiavel aqui.
            Write-Warning "$(Split-Path $file -Leaf) assinado, mas o certificado nao e confiavel neste computador: $($signature.StatusMessage)"
        }
        else {
            Write-Host "$(Split-Path $file -Leaf) assinado por $($certificate.Subject)."
        }
    }
}
finally {
    $certificate.Dispose()
}
