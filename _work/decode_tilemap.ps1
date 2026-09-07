# Decode les tile_map_data de Scenes/Main.tscn et resume les tuiles utilisees.
# Format Godot 4 : entete int16 (version), puis 12 octets par cellule :
# int16 x, int16 y, int16 source_id, int16 atlas_x, int16 atlas_y, int16 alternative.
$scene = Join-Path (Split-Path -Parent $PSScriptRoot) 'Scenes/Main.tscn'
$text = Get-Content -Raw $scene
$layer = $null
foreach ($line in ($text -split "`r?`n")) {
    if ($line -match '^\[node name="([^"]+)"') { $layer = $Matches[1] }
    if ($line -match '^tile_map_data = PackedByteArray\("([^"]*)"\)') {
        $bytes = [Convert]::FromBase64String($Matches[1])
        $seen = @{}
        for ($i = 2; $i + 11 -lt $bytes.Length; $i += 12) {
            $src = [BitConverter]::ToInt16($bytes, $i + 4)
            $ax = [BitConverter]::ToInt16($bytes, $i + 6)
            $ay = [BitConverter]::ToInt16($bytes, $i + 8)
            $alt = [BitConverter]::ToInt16($bytes, $i + 10)
            $k = "{0}:{1},{2} (alt {3})" -f $src, $ax, $ay, $alt
            $seen[$k] = 1 + $(if ($seen.ContainsKey($k)) { $seen[$k] } else { 0 })
        }
        $cells = [math]::Floor(($bytes.Length - 2) / 12)
        Write-Output "$layer : $cells cellules, entete $([BitConverter]::ToInt16($bytes,0))"
        foreach ($k in ($seen.Keys | Sort-Object)) { Write-Output ("    {0} x{1}" -f $k, $seen[$k]) }
    }
}
