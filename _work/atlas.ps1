# Assemble Assets/atlas.png a partir des PNG individuels de Assets/.
#
# Grille 8 x 8 de tuiles 32 x 32, soit 256 x 256, sans separation.
# La rangee 0 reproduit exactement les coordonnees heritees du MVP
# (0:2,0 a 0:7,0) : le tile_map_data de Scenes/Main.tscn survit intact.
#
# Les portes ne sont pas dans l'atlas : elles gardent leurs sources propres
# 1 et 2, pour la meme raison.
#
# Relancer apres toute modification d'un PNG source :
#   powershell -ExecutionPolicy Bypass -File _work/atlas.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$tile = 32
$cols = 8
$rows = 8

# $null = emplacement laisse libre.
$layout = @(
    # rangee 0 — tuiles heritees, coordonnees gelees
    $null, $null,
    'Items/Medicine', 'Structures/WindowBroken', 'Structures/Window',
    'Structures/Wall', 'Floors/FloorIndoor', 'Floors/FloorOutdoor',

    # rangee 1 — sols
    'Floors/FloorAsphalt', 'Floors/FloorSidewalk', 'Floors/FloorGrass',
    'Floors/FloorTiled', 'Floors/FloorDirty', 'Floors/FloorPharmacy',
    $null, $null,

    # rangee 2 — decor bloquant
    'Obstacles/ObstacleCarWreck', 'Obstacles/ObstacleRubble', 'Obstacles/ObstacleCounter',
    'Obstacles/ObstacleShelfFull', 'Obstacles/ObstacleBin',
    $null, $null, $null,

    # rangee 3 — objets au sol
    'Items/ItemCannedFood', 'Items/ItemWaterCan', 'Items/ItemBandage',
    'Items/ItemFirstAidKit', 'Items/ItemFuelCan', 'Items/ItemAmmoBox',
    'Items/ItemFlashlight', 'Items/ItemRadio',

    # rangee 4 — objets au sol (suite) et armes
    'Items/ItemFirecracker', 'Items/ItemToolbox',
    'Items/WeaponRevolver', 'Items/WeaponShotgun', 'Items/WeaponBaseballBat',
    'Items/WeaponCrowbar', 'Items/WeaponMachete',
    $null,

    # rangee 5 — camp : nourriture et eau, chaque reserve en triple contigu
    'Camp/CampFoodStore_Full', 'Camp/CampFoodStore_Half', 'Camp/CampFoodStore_Empty',
    $null,
    'Camp/CampWaterStore_Full', 'Camp/CampWaterStore_Low', 'Camp/CampWaterStore_Empty',
    $null,

    # rangee 6 — camp : essence et mobilier
    'Camp/CampFuelStore_Full', 'Camp/CampFuelStore_Low', 'Camp/CampFuelStore_Empty',
    $null,
    'Camp/CampFire', 'Camp/CampTent', 'Camp/CampWorkbench',
    $null,

    # rangee 7 — tuiles de zone
    'Others/FuelPump', 'Others/FuelPumpEmpty', 'Others/WindowBarred',
    'Others/ShutterHalf', 'Others/Cot', 'Others/Locker',
    $null, $null
)

if ($layout.Count -ne ($cols * $rows)) {
    throw "Le plan compte $($layout.Count) emplacements, attendu $($cols * $rows)."
}

# Largeur et hauteur precalculees : en PowerShell la virgule lie plus fort
# que la multiplication, et « $cols * $tile, $rows * $tile » ne veut pas dire
# ce qu'on croit.
$width = $cols * $tile
$height = $rows * $tile

$atlas = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($atlas)
# SourceCopy : on recopie l'alpha tel quel, sans fondu ni interpolation.
$g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$used = @()

for ($i = 0; $i -lt $layout.Count; $i++) {
    $name = $layout[$i]
    if ($null -eq $name) { continue }

    $path = Join-Path $root "Assets/$name.png"
    if (-not (Test-Path $path)) { throw "Introuvable : $path" }

    $img = [System.Drawing.Image]::FromFile($path)
    try {
        if ($img.Width -ne $tile -or $img.Height -ne $tile) {
            throw "$name fait $($img.Width) x $($img.Height), attendu $tile x $tile."
        }
        $x = ($i % $cols) * $tile
        $y = [math]::Floor($i / $cols) * $tile
        $g.DrawImage($img, (New-Object System.Drawing.Rectangle($x, $y, $tile, $tile)))
        $used += "{0}:{1}" -f ($i % $cols), [math]::Floor($i / $cols)
    }
    finally { $img.Dispose() }
}

$g.Dispose()
$out = Join-Path $root 'Assets/atlas.png'
$atlas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$atlas.Dispose()

Write-Output "atlas.png : $($used.Count) tuiles placees sur $($cols * $rows) emplacements."
Write-Output ($used -join ' ')
