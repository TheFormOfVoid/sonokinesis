# Finds the Just Shapes & Beats folder through Steam's library list. Shared by the install scripts.
function Find-Game {
    $candidates = New-Object System.Collections.Generic.List[string]
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    $roots = @()
    if ($steam) { $roots += ($steam -replace '/', '\') }
    $roots += 'C:\Program Files (x86)\Steam'
    foreach ($root in ($roots | Select-Object -Unique)) {
        $candidates.Add($root)
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -Raw $vdf), '"path"\s+"([^"]+)"')) {
                $candidates.Add(($m.Groups[1].Value -replace '\\\\', '\'))
            }
        }
    }
    foreach ($lib in ($candidates | Select-Object -Unique)) {
        $dir = Join-Path $lib 'steamapps\common\Just Shapes & Beats'
        if (Test-Path (Join-Path $dir 'JSB.exe')) { return $dir }
    }
    return $null
}
