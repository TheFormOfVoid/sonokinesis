<#
.SYNOPSIS
  Adds custom levels to the game: drop level .zip files or level folders onto Add-Level.bat.

.DESCRIPTION
  Each level (a .zip, a folder with a level.json, or a level.json itself) is copied into the custom
  levels folder (Just Shapes & Beats\CustomLevels, or the one set in the loader's settings). A level
  that is already there is replaced. Without anything dropped on it, it opens the custom levels folder.
#>
[CmdletBinding(PositionalBinding = $false)]   # dropped paths must never land in -GameDir
param(
    [Parameter()][string]$GameDir,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Paths
)

$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'Find-Game.ps1')

function Fail($text) { Write-Host ""; Write-Host "  $text" -ForegroundColor Red; Write-Host ""; exit 1 }

Write-Host ""
Write-Host "  Add Sonokinesis levels" -ForegroundColor Magenta
Write-Host ""

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'JSB.exe'))) {
    Fail "Could not find Just Shapes & Beats. Run again with -GameDir 'path\to\Just Shapes & Beats'."
}

# The levels folder: the loader's setting if one was chosen, else <game>\CustomLevels.
$levels = Join-Path $GameDir 'CustomLevels'
$cfg = Join-Path $GameDir 'BepInEx\config\dev.sonokinesis.loader.cfg'
if (Test-Path $cfg) {
    $m = Select-String -Path $cfg -Pattern '^\s*LevelsFolder\s*=\s*(.+?)\s*$' | Select-Object -First 1
    if ($m -and $m.Matches[0].Groups[1].Value) { $levels = $m.Matches[0].Groups[1].Value }
}
New-Item -ItemType Directory -Force $levels | Out-Null

if (-not $Paths -or $Paths.Count -eq 0) {
    Write-Host "  Drop level .zip files or level folders onto Add-Level.bat to add them."
    Write-Host "  Your levels folder: $levels"
    Start-Process explorer.exe $levels
    exit 0
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$added = 0
foreach ($p in $Paths) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Host "  Not found: $p" -ForegroundColor Yellow; continue }
    $item = Get-Item -LiteralPath $p
    if (-not $item.PSIsContainer -and $item.Name -ieq 'level.json') { $item = Get-Item -LiteralPath $item.DirectoryName }

    if ($item.PSIsContainer) {
        if (-not (Test-Path (Join-Path $item.FullName 'level.json'))) {
            Write-Host "  Skipped '$($item.Name)': no level.json inside." -ForegroundColor Yellow; continue
        }
        $dest = Join-Path $levels $item.Name
        if ($item.FullName.TrimEnd('\') -ieq $dest.TrimEnd('\')) { Write-Host "  '$($item.Name)' is already in the levels folder."; continue }
        $existed = Test-Path $dest
        if ($existed) { Remove-Item -Recurse -Force $dest }
        Copy-Item -Recurse $item.FullName $dest
        Write-Host ("  {0} '{1}'." -f ($(if ($existed) { 'Updated' } else { 'Added' }), $item.Name)) -ForegroundColor Green
        $added++
        continue
    }

    if ($item.Extension -ieq '.zip') {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($item.FullName)
        try {
            # level.json at the top or inside one folder (zips made on Windows may use backslashes).
            $hasLevel = $zip.Entries | Where-Object { $n = $_.FullName.Replace('\', '/'); $n -match '(^|/)level\.json$' -and ($n.Split('/').Count -le 2) }
        }
        finally { $zip.Dispose() }
        if (-not $hasLevel) { Write-Host "  Skipped '$($item.Name)': it is not a level (no level.json inside)." -ForegroundColor Yellow; continue }
        $dest = Join-Path $levels $item.Name
        $existed = Test-Path $dest
        Copy-Item -Force $item.FullName $dest
        Write-Host ("  {0} '{1}'." -f ($(if ($existed) { 'Updated' } else { 'Added' }), $item.Name)) -ForegroundColor Green
        $added++
        continue
    }

    Write-Host "  Skipped '$($item.Name)': drop a level .zip or a level folder." -ForegroundColor Yellow
}

Write-Host ""
if ($added -gt 0) {
    Write-Host "  Done. In the game, press F1 on the main menu to play (F5 in the level list refreshes it)."
}
Write-Host "  Levels folder: $levels"
Write-Host ""
