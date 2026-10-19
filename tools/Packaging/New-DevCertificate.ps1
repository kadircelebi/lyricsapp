<#
.SYNOPSIS
    Creates (or reuses) a self-signed code-signing certificate for local MSIX test builds.

.DESCRIPTION
    The subject is the Publisher from src/LyricsApp/Package.appxmanifest, as MSIX requires.
    The certificate and its private key stay in Cert:\CurrentUser\My. Only the public part is
    exported, to do_not_share\signing\LyricsApp-dev.cer (git-ignored), for trusting it on a test PC.
    Never use this certificate for the Store: Partner Center signs Store packages itself.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
[xml]$manifest = Get-Content (Join-Path $root 'src\LyricsApp\Package.appxmanifest') -Raw
$publisher = $manifest.Package.Identity.Publisher

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
        -FriendlyName 'LyricsApp MSIX test signing' `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(2)
    Write-Host "Created certificate $($cert.Thumbprint) for $publisher"
} else {
    Write-Host "Reusing certificate $($cert.Thumbprint) for $publisher"
}

$outDir = Join-Path $root 'do_not_share\signing'
New-Item -ItemType Directory -Force $outDir | Out-Null
$cer = Join-Path $outDir 'LyricsApp-dev.cer'
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Write-Host "Public certificate: $cer"
$cert.Thumbprint
