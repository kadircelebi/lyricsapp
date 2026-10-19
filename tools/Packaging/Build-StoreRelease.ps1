<#
.SYNOPSIS
    Prepares everything for a Microsoft Store submission in dist\store\.

.DESCRIPTION
    - Version comes from src\LyricsApp\Package.appxmanifest (Identity/@Version, e.g. 1.0.0.0 -> 1.0.0).
    - If dist\store holds a different version, it is moved to dist\old_versions\<that version>\ first.
      Rebuilding the same version replaces dist\store.
    - Builds the unsigned x64 + ARM64 .msix (the Store signs them) into dist\store\packages\.
    - Copies the listing texts (docs\store-listing\<lang>\*.txt), the release texts
      (docs\releases\<version>\*.txt) and the privacy policy.
    - Generates poster/box art and captures the screenshots (tr + en) into dist\store\listing\<lang>\.

.EXAMPLE
    .\tools\Packaging\Build-StoreRelease.ps1
    .\tools\Packaging\Build-StoreRelease.ps1 -SkipScreenshots     # texts + packages + logos only
#>
[CmdletBinding()]
param(
    [switch] $SkipScreenshots,
    [switch] $SkipPackages
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifest = Join-Path $root 'src\LyricsApp\Package.appxmanifest'
$fullVersion = ([xml](Get-Content $manifest -Raw -Encoding UTF8)).Package.Identity.Version
$version = ($fullVersion -split '\.')[0..2] -join '.'
$releaseDocs = Join-Path $root "docs\releases\$version"
if (-not (Test-Path (Join-Path $releaseDocs 'certification-notes.txt'))) {
    throw "docs\releases\$version\certification-notes.txt is missing (see docs\releases\README.md)"
}

$dist = Join-Path $root 'dist'
$store = Join-Path $dist 'store'
$old = Join-Path $dist 'old_versions'
$versionFile = Join-Path $store 'VERSION.txt'

# 1. Archive the previous release (never deleted).
if (Test-Path $store) {
    $previous = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { 'unknown' }
    if ($previous -ne $version) {
        New-Item -ItemType Directory -Force $old | Out-Null
        $target = Join-Path $old $previous
        if (Test-Path $target) { $target = "$target-$(Get-Date -Format yyyyMMdd-HHmmss)" }
        Move-Item $store $target
        Write-Host "Moved previous release $previous to $target"
    }
    else {
        Remove-Item $store -Recurse -Force
        Write-Host "Rebuilding $version (replacing dist\store)"
    }
}
New-Item -ItemType Directory -Force $store | Out-Null
Set-Content $versionFile $version -Encoding ASCII

# 2. Packages.
if (-not $SkipPackages) {
    & (Join-Path $PSScriptRoot 'Build-Msix.ps1') -Unsigned | Out-Null
    $packages = New-Item -ItemType Directory -Force (Join-Path $store 'packages')
    Get-ChildItem (Join-Path $root 'artifacts\msix') -Recurse -Filter "*_${fullVersion}_*.msix" |
        Where-Object { $_.FullName -notmatch '\\Dependencies\\' } |
        ForEach-Object { Copy-Item $_.FullName $packages.FullName }
    if (-not (Get-ChildItem $packages.FullName -Filter *.msix)) { throw "No $fullVersion .msix found under artifacts\msix" }
}

# 3. Texts.
foreach ($lang in 'tr', 'en') {
    $dest = New-Item -ItemType Directory -Force (Join-Path $store "listing\$lang")
    Copy-Item (Join-Path $root "docs\store-listing\$lang\*.txt") $dest.FullName
    $whatsNew = Join-Path $releaseDocs "whats-new.$lang.txt"
    if (Test-Path $whatsNew) { Copy-Item $whatsNew (Join-Path $dest.FullName 'whats-new.txt') }
}
Copy-Item (Join-Path $releaseDocs 'certification-notes.txt') $store
Copy-Item (Join-Path $root 'docs\store-listing\privacy-policy.md') $store

# 4. Images.
$listing = Join-Path $store 'listing'
python (Join-Path $PSScriptRoot 'make_listing_images.py') $listing
if ($LASTEXITCODE -ne 0) { throw 'make_listing_images.py failed' }
if (-not $SkipScreenshots) {
    $shotsApp = Join-Path $root 'artifacts\screenshot-app'
    dotnet build (Join-Path $root 'src\LyricsApp\LyricsApp.csproj') -c Release -o $shotsApp -nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Release build for screenshots failed' }
    & (Join-Path $PSScriptRoot 'Capture-StoreScreenshots.ps1') -Exe (Join-Path $shotsApp 'LyricsApp.exe') -ListingDir $listing
}
Remove-Item (Join-Path $listing '_backdrops') -Recurse -Force -ErrorAction SilentlyContinue

# 5. Where everything goes.
@"
MrW Lyrics $version - Microsoft Store gönderimi

packages\                     Packages > yükle (x64 ve ARM64 .msix, ikisi birden)
certification-notes.txt        Submission options > Notes for certification
privacy-policy.md              sitede yayında: https://lyrics.mrwproject.com/privacy/ > Properties > Privacy policy URL
                               Destek / web sitesi: https://lyrics.mrwproject.com/contact/
listing\<tr|en>\description.txt        Store listing > Description
listing\<tr|en>\short-description.txt  Store listing > Short description
listing\<tr|en>\features.txt           Store listing > Product features (satır başına bir madde)
listing\<tr|en>\keywords.txt           Store listing > Search terms (satır başına bir kelime)
listing\<tr|en>\whats-new.txt          Store listing > What's new in this version (yalnız güncellemelerde)
listing\<tr|en>\screenshots\*.png      Store listing > Screenshots (1920x1080, sırayla 1-4)
listing\<tr|en>\logos\poster-*.png     Store listing > Store logos > 9:16 Poster art
listing\<tr|en>\logos\boxart-*.png     Store listing > Store logos > 1:1 Box art
listing\<tr|en>\logos\apptile-300x300.png  Store listing > Store logos > 1:1 App tile icon

Ayrıntılar: docs\store-listing\README.md ve docs\releases\README.md
"@ | Set-Content (Join-Path $store 'README.txt') -Encoding UTF8

Write-Host "Store release $version ready in $store"
Get-ChildItem $store -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($store.Length + 1) }
