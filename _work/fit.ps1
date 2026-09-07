# Crop a sprite to its opaque bounding box and fit it into a target canvas.
# Nearest-neighbour only: this is pixel art, no interpolation allowed.
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Dst,
    [Parameter(Mandatory = $true)][int]$W,
    [Parameter(Mandatory = $true)][int]$H,
    [int]$Zoom = 0,            # if >0, also write a zoomed preview next to $Dst
    [switch]$BottomAlign,      # feet on the floor rather than vertically centred
    [switch]$LeftAlign         # grip flush left: held weapons pivot around it, so it
                               # must land on the same pixel for all five of them
)

Add-Type -AssemblyName System.Drawing

$srcImg = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Src).Path)
$bmp = New-Object System.Drawing.Bitmap $srcImg
$srcImg.Dispose()

# --- opaque bounding box ---
$minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        if ($bmp.GetPixel($x, $y).A -gt 8) {
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
if ($maxX -lt 0) { throw "$Src is fully transparent" }

$cw = $maxX - $minX + 1
$ch = $maxY - $minY + 1
$crop = $bmp.Clone(
    (New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch),
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bmp.Dispose()

# --- scale down only if the content overflows the target ---
$scale = [Math]::Min($W / $cw, $H / $ch)
if ($scale -gt 1) { $scale = 1 }          # never upscale: keep native pixel density
$dw = [Math]::Max(1, [int][Math]::Floor($cw * $scale))
$dh = [Math]::Max(1, [int][Math]::Floor($ch * $scale))

$out = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($out)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighSpeed
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

$ox = if ($LeftAlign) { 0 } else { [int](($W - $dw) / 2) }
$oy = if ($BottomAlign) { $H - $dh } else { [int](($H - $dh) / 2) }
$g.DrawImage($crop, $ox, $oy, $dw, $dh)
$g.Dispose()
$crop.Dispose()

New-Item -ItemType Directory -Force (Split-Path $Dst) | Out-Null
$out.Save((Join-Path (Get-Location) $Dst), [System.Drawing.Imaging.ImageFormat]::Png)

if ($Zoom -gt 0) {
    $z = New-Object System.Drawing.Bitmap ($W * $Zoom), ($H * $Zoom), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $zg = [System.Drawing.Graphics]::FromImage($z)
    $zg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $zg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $zg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $zg.DrawImage($out, 0, 0, $W * $Zoom, $H * $Zoom)
    $zg.Dispose()
    # Previews are a review aid, never a deliverable: keep them out of Assets/.
    New-Item -ItemType Directory -Force "_work/preview" | Out-Null
    $zpath = "_work/preview/" + [IO.Path]::GetFileNameWithoutExtension($Dst) + "_zoom.png"
    $z.Save((Join-Path (Get-Location) $zpath), [System.Drawing.Imaging.ImageFormat]::Png)
    $z.Dispose()
}

$out.Dispose()
"{0} -> {1} ({2}x{3} content {4}x{5})" -f $Src, $Dst, $W, $H, $cw, $ch
