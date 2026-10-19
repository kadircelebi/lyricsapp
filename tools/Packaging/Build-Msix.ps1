<#
.SYNOPSIS
    Builds the MSIX packages into artifacts\msix\ (one per platform).

.EXAMPLE
    .\tools\Packaging\Build-Msix.ps1                    # x64 + ARM64, signed with the local test certificate
    .\tools\Packaging\Build-Msix.ps1 -Platforms x64
    .\tools\Packaging\Build-Msix.ps1 -Unsigned          # for Partner Center upload (the Store signs it)

.NOTES
    The test certificate comes from New-DevCertificate.ps1. See docs/store-packaging.md.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string[]]$Platforms = @('x64', 'ARM64'),
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$project = Join-Path $root 'src\LyricsApp\LyricsApp.csproj'

$signing = @('-p:AppxPackageSigningEnabled=false')
if (-not $Unsigned) {
    $thumbprint = & (Join-Path $PSScriptRoot 'New-DevCertificate.ps1') | Select-Object -Last 1
    $signing = @('-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateThumbprint=$thumbprint")
}

foreach ($platform in $Platforms) {
    $rid = "win-$($platform.ToLowerInvariant())"
    Write-Host "== $platform ($rid)"
    dotnet publish $project -c Release -r $rid "-p:Platform=$platform" -p:GenerateAppxPackageOnBuild=true @signing
    if ($LASTEXITCODE -ne 0) { throw "MSIX build failed for $platform" }
}

Get-ChildItem (Join-Path $root 'artifacts\msix') -Recurse -Filter *.msix | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
