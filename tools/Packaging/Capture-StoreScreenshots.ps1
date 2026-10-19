<#
.SYNOPSIS
    Captures the Store screenshots (1920x1080, Turkish and English) with the app in demo mode.

.DESCRIPTION
    For each language and shot: shows the backdrop made by make_listing_images.py in a topmost window,
    starts LyricsApp in demo mode (made-up song, original lyrics, no network) on top of it with its own
    temporary settings, and saves the backdrop area as PNG. Runs on the secondary monitor when there is
    one, so the main screen stays usable. The real settings are never touched (LYRICSAPP_SETTINGS_DIR).

.EXAMPLE
    .\tools\Packaging\Capture-StoreScreenshots.ps1 -Exe src\LyricsApp\bin\Release\...\LyricsApp.exe -ListingDir dist\store\listing
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $ListingDir,
    [string[]] $Languages = @('tr', 'en')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class StoreShotNative {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[StoreShotNative]::SetProcessDPIAware() | Out-Null

$Exe = (Resolve-Path $Exe).Path
$ListingDir = (Resolve-Path $ListingDir).Path
$backdrops = Join-Path $ListingDir '_backdrops'

# Physical pixels per WPF unit (the app positions itself in DIPs).
$g = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero); $scale = $g.DpiX / 96.0; $g.Dispose()
$screen = [System.Windows.Forms.Screen]::AllScreens | Sort-Object Primary | Select-Object -First 1   # non-primary first
$originX = $screen.Bounds.X; $originY = $screen.Bounds.Y
if ($screen.Bounds.Width -lt 1920 -or $screen.Bounds.Height -lt 1080) { throw "Screen $($screen.DeviceName) is smaller than 1920x1080" }

function Dip([double] $physical) { [math]::Round($physical / $scale, 1) }

# Each shot: window rectangle inside the 1920x1080 backdrop (physical px), display settings,
# and the demo playback position at capture time (seconds into the demo song).
$shots = [ordered]@{
    '1-lyrics'   = @{ X = 1060; Y = 70;  W = 720;  H = 940; At = 47.5; Display = @{ FocusMode = $false } }
    '2-focus'    = @{ X = 1060; Y = 70;  W = 720;  H = 940; At = 41.5; Display = @{ FocusMode = $true; ContextLines = 2 } }
    '3-floating' = @{ X = 180;  Y = 370; W = 1560; H = 580; At = 42.6; Display = @{ LyricsOnly = $true; FocusMode = $true; ContextLines = 1; LineFill = $true; FontSize = 40;
                                                                              ActiveColor = '#FFFFEB3B'; InactiveColor = '#FFFFFFFF'; ShadowEnabled = $true; ShadowColor = '#FF000000'; ShadowBlur = 8; ShadowDepth = 0; ShadowOpacity = 1 } }
    # Settings is moved next to the main window after it opens (it would otherwise cover it).
    '4-style'    = @{ X = 690;  Y = 70;  W = 570;  H = 940; At = 41.5; SettingsAt = @(1290, 40, 600, 1000); Display = @{ FocusMode = $true; ContextLines = 2; ActiveColor = '#FF4DD0E1'; InactiveColor = '#FFB39DFF';
                                                                              ShadowColor = '#FF00E5FF'; ShadowBlur = 18; ShadowDepth = 0; ShadowOpacity = 0.85 }; OpenSettings = $true }
}
$waitSeconds = 6

foreach ($lang in $Languages) {
    $outDir = Join-Path $ListingDir "$lang\screenshots"
    New-Item -ItemType Directory -Force $outDir | Out-Null

    foreach ($name in $shots.Keys) {
        $shot = $shots[$name]
        $backdropPath = Join-Path $backdrops "$lang-$name.png"
        if (-not (Test-Path $backdropPath)) { throw "Missing backdrop $backdropPath (run make_listing_images.py first)" }

        $form = New-Object System.Windows.Forms.Form
        $form.FormBorderStyle = 'None'; $form.StartPosition = 'Manual'; $form.ShowInTaskbar = $false; $form.TopMost = $true
        $form.Location = New-Object System.Drawing.Point $originX, $originY
        $form.Size = New-Object System.Drawing.Size 1920, 1080
        $form.BackgroundImage = [System.Drawing.Image]::FromFile($backdropPath)
        $form.BackgroundImageLayout = 'None'
        $form.Show(); [System.Windows.Forms.Application]::DoEvents()

        $settingsDir = Join-Path ([IO.Path]::GetTempPath()) ("lyricsapp-shot-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory $settingsDir | Out-Null
        $display = @{ Topmost = $true } + $shot.Display
        $settings = @{
            LyricsFolder = $settingsDir
            Left = Dip ($originX + $shot.X); Top = Dip ($originY + $shot.Y); Width = Dip $shot.W; Height = Dip $shot.H
            Display = $display
        }
        $settings | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $settingsDir 'settings.json') -Encoding UTF8

        $psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
        $psi.UseShellExecute = $false
        $psi.EnvironmentVariables['LYRICSAPP_SETTINGS_DIR'] = $settingsDir
        $psi.EnvironmentVariables['LYRICSAPP_LANGUAGE'] = $lang
        $psi.EnvironmentVariables['LYRICSAPP_DEMO'] = '1'
        $psi.EnvironmentVariables['LYRICSAPP_DEMO_POSITION'] = ([string]($shot.At - $waitSeconds)).Replace(',', '.')
        $app = [System.Diagnostics.Process]::Start($psi)
        try {
            $until = (Get-Date).AddSeconds($waitSeconds)
            $opened = $false
            while ((Get-Date) -lt $until) {
                [System.Windows.Forms.Application]::DoEvents()
                if ($shot.OpenSettings -and -not $opened -and (Get-Date) -gt $until.AddSeconds(-3)) {
                    $app.Refresh()
                    $root = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
                    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), 'SettingsButton'
                    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
                    if ($button) { $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                    $opened = $true
                    if ($shot.SettingsAt) {
                        Start-Sleep -Milliseconds 700
                        # The settings window is owned by the main window, so it sits under it in the UIA tree.
                        $windowCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Window)
                        $settingsWindow = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $windowCond)
                        if ($settingsWindow) {
                            $transform = $settingsWindow.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
                            $x, $y, $w, $h = $shot.SettingsAt
                            $transform.Resize($w, $h)
                            $transform.Move($originX + $x, $originY + $y)
                        }
                    }
                }
                Start-Sleep -Milliseconds 50
            }
            $app.Refresh(); if ($app.HasExited) { throw "LyricsApp exited ($($app.ExitCode)) during $lang/$name" }

            $bmp = New-Object System.Drawing.Bitmap 1920, 1080
            $gfx = [System.Drawing.Graphics]::FromImage($bmp)
            $gfx.CopyFromScreen($originX, $originY, 0, 0, $bmp.Size)
            $gfx.Dispose()
            $file = Join-Path $outDir "$name.png"
            $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            Write-Host "  $lang/$name.png"
        }
        finally {
            if (-not $app.HasExited) { $null = $app.CloseMainWindow(); if (-not $app.WaitForExit(3000)) { $app.Kill() } }
            $form.Close(); $form.BackgroundImage.Dispose(); $form.Dispose()
            Remove-Item $settingsDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
