<#
.SYNOPSIS
  Removes the Beatform loader from Just Shapes & Beats.

.DESCRIPTION
  Removes the Beatform plugin. If the Beatform installer also installed BepInEx and no other BepInEx
  plugins are present, BepInEx is removed too, leaving the game folder as it was. Custom levels are
  kept unless -RemoveLevels is given. Run it with the game closed.
#>
param(
    [string]$GameDir,
    [switch]$RemoveLevels
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Find-Game.ps1')

function Fail($text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; Write-Host ""; exit 1 }

Write-Host ""
Write-Host "  Beatform loader uninstaller" -ForegroundColor Magenta
Write-Host ""
if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'JSB.exe'))) { Fail "Could not find Just Shapes & Beats. Run again with -GameDir." }
if (Get-Process -Name 'JSB' -ErrorAction SilentlyContinue) { Fail "Just Shapes & Beats is running. Close it first." }

$bepinex = Join-Path $GameDir 'BepInEx'
$manifest = Join-Path $bepinex 'beatform\install-manifest.txt'
$added = @()
if (Test-Path $manifest) { $added = Get-Content $manifest }

$pluginDir = Join-Path $bepinex 'plugins\Beatform'
if (Test-Path $pluginDir) { Remove-Item -Recurse -Force $pluginDir; Write-Host "  Removed the Beatform plugin." }

$otherPlugins = @()
if (Test-Path (Join-Path $bepinex 'plugins')) { $otherPlugins = Get-ChildItem -Force (Join-Path $bepinex 'plugins') }

if ($added -contains '#bepinex-installed-by-beatform') {
    if ($otherPlugins.Count -gt 0) {
        Write-Host "  Other BepInEx plugins are installed, so BepInEx stays."
        Remove-Item -Recurse -Force (Join-Path $bepinex 'beatform') -ErrorAction SilentlyContinue
    } else {
        foreach ($name in $added) {
            if ($name.StartsWith('#')) { continue }
            $path = Join-Path $GameDir $name
            if (Test-Path $path) { Remove-Item -Recurse -Force $path }
        }
        Write-Host "  Removed BepInEx (it was installed by Beatform)."
    }
} else {
    Remove-Item -Recurse -Force (Join-Path $bepinex 'beatform') -ErrorAction SilentlyContinue
    Write-Host "  BepInEx was there before Beatform, so it stays."
}

$levels = Join-Path $GameDir 'CustomLevels'
if ($RemoveLevels -and (Test-Path $levels)) { Remove-Item -Recurse -Force $levels; Write-Host "  Removed the CustomLevels folder." }
elseif (Test-Path $levels) { Write-Host "  Your levels in $levels were kept." }

Write-Host ""
Write-Host "  Done." -ForegroundColor Green
Write-Host ""
