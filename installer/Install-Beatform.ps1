<#
.SYNOPSIS
  Installs (or updates) the Beatform loader into Just Shapes & Beats.

.DESCRIPTION
  Copies the BepInEx mod loader (only if the game does not have it yet) and the Beatform plugin into
  the game folder, and creates a CustomLevels folder with the demo level. No game file is changed or
  replaced: everything is added next to the game, and Uninstall-Beatform.ps1 removes exactly what this
  script added. Run it with the game closed.

.PARAMETER GameDir
  The game folder (the one with JSB.exe). Found automatically through Steam when left out.
#>
param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $here 'loader'
$manifestName = 'install-manifest.txt'
. (Join-Path $here 'Find-Game.ps1')

function Write-Step($text) { Write-Host "  $text" }
function Fail($text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; Write-Host ""; exit 1 }

Write-Host ""
Write-Host "  Beatform loader installer" -ForegroundColor Magenta
Write-Host ""

if (-not (Test-Path (Join-Path $payload 'plugin\Beatform.Loader.dll'))) {
    Fail "The loader files are missing. Run this script from the unpacked Beatform folder."
}

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'JSB.exe'))) {
    Fail "Could not find Just Shapes & Beats. Run again with -GameDir 'path\to\Just Shapes & Beats'."
}
Write-Step "Game folder: $GameDir"

if (Get-Process -Name 'JSB' -ErrorAction SilentlyContinue) {
    Fail "Just Shapes & Beats is running. Close it and run the installer again."
}

$bepinex = Join-Path $GameDir 'BepInEx'
$dataDir = Join-Path $bepinex 'beatform'
$manifest = Join-Path $dataDir $manifestName
$added = New-Object System.Collections.Generic.List[string]
if (Test-Path $manifest) { $added.AddRange([string[]](Get-Content $manifest)) }

# 1. BepInEx (the mod loader Beatform runs on). Installed only when the game has none.
$il2cppCore = Join-Path $bepinex 'core\BepInEx.Unity.IL2CPP.dll'
if (Test-Path $il2cppCore) {
    Write-Step "BepInEx is already installed, keeping it."
} elseif (Test-Path $bepinex) {
    Fail "The game has a BepInEx folder that is not BepInEx 6 for IL2CPP. Remove it first or install BepInEx 6 IL2CPP yourself."
} else {
    Write-Step "Installing BepInEx 6 (IL2CPP)..."
    $src = Join-Path $payload 'bepinex'
    foreach ($item in Get-ChildItem -Force $src) {
        $target = Join-Path $GameDir $item.Name
        if (Test-Path $target) { Fail "Unexpected existing file in the game folder: $target" }
        Copy-Item -Recurse -Force $item.FullName $target
        $added.Add($item.Name)
    }
    $added.Add('#bepinex-installed-by-beatform')
}

# 2. The Beatform plugin (always replaced, so this also updates).
$pluginDir = Join-Path $bepinex 'plugins\Beatform'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item -Force (Join-Path $payload 'plugin\*') $pluginDir
Write-Step "Installed the Beatform plugin."

# 3. A folder for custom levels, with the demo level.
$levels = Join-Path $GameDir 'CustomLevels'
if (-not (Test-Path $levels)) {
    New-Item -ItemType Directory $levels | Out-Null
    $added.Add('#created-CustomLevels')
}
$demoSrc = Join-Path $payload 'levels'
if (Test-Path $demoSrc) {
    foreach ($level in Get-ChildItem -Directory $demoSrc) {
        $dest = Join-Path $levels $level.Name
        if (-not (Test-Path $dest)) { Copy-Item -Recurse $level.FullName $dest; Write-Step "Added the demo level '$($level.Name)'." }
    }
}

New-Item -ItemType Directory -Force $dataDir | Out-Null
$added | Select-Object -Unique | Set-Content -Encoding UTF8 $manifest

Write-Host ""
Write-Host "  Done." -ForegroundColor Green
Write-Host ""
Write-Host "  Start the game. The first start after installing takes a minute or two while BepInEx"
Write-Host "  prepares itself (a console window may show progress). On the main menu press F1 to open"
Write-Host "  the custom level list. Put levels in: $levels"
Write-Host ""
