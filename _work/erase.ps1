# Derive a depleted store state by erasing regions from the full state.
# Spec F 4.6: the three states of a store must be pixel-superposable, so they are
# produced by erasing from one drawing rather than by three separate generations.
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Dst,
    # Rectangles to clear, each "x,y,w,h" in sprite pixels.
    [Parameter(Mandatory = $true)][string[]]$Clear,
    # Optional "#rrggbb" to paint instead of clearing. Needed when the region sits
    # inside a solid body (a tank's water level): clearing there punches a hole.
    [string]$FillColor = $null,
    [int]$Zoom = 0
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Src).Path)
$bmp = New-Object System.Drawing.Bitmap $img
$img.Dispose()

if ($FillColor) {
    $h = $FillColor.TrimStart('#')
    $blank = [System.Drawing.Color]::FromArgb(255,
        [Convert]::ToInt32($h.Substring(0, 2), 16),
        [Convert]::ToInt32($h.Substring(2, 2), 16),
        [Convert]::ToInt32($h.Substring(4, 2), 16))
} else {
    $blank = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
}
foreach ($r in $Clear) {
    $p = $r.Split(',') | ForEach-Object { [int]$_.Trim() }
    for ($y = $p[1]; $y -lt ($p[1] + $p[3]); $y++) {
        for ($x = $p[0]; $x -lt ($p[0] + $p[2]); $x++) {
            if ($x -ge 0 -and $y -ge 0 -and $x -lt $bmp.Width -and $y -lt $bmp.Height) {
                $bmp.SetPixel($x, $y, $blank)
            }
        }
    }
}

New-Item -ItemType Directory -Force (Split-Path $Dst) | Out-Null
$bmp.Save((Join-Path (Get-Location) $Dst), [System.Drawing.Imaging.ImageFormat]::Png)

if ($Zoom -gt 0) {
    New-Item -ItemType Directory -Force "_work/preview" | Out-Null
    $z = New-Object System.Drawing.Bitmap ($bmp.Width * $Zoom), ($bmp.Height * $Zoom), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($z)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.DrawImage($bmp, 0, 0, $bmp.Width * $Zoom, $bmp.Height * $Zoom)
    $g.Dispose()
    $zp = "_work/preview/" + [IO.Path]::GetFileNameWithoutExtension($Dst) + "_zoom.png"
    $z.Save((Join-Path (Get-Location) $zp), [System.Drawing.Imaging.ImageFormat]::Png)
    $z.Dispose()
}

$bmp.Dispose()
"$Dst written"
