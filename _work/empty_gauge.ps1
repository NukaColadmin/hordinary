# Build a gauge's empty state from its full state.
# The generator would not reliably draw "empty" bars, and deriving keeps the empty
# track pixel-identical in shape to the full one - which is what a gauge needs.
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Dst,
    [double]$Factor = 0.28,   # how far to darken the lit fill
    [int]$LumaCut = 90        # above this luminance a pixel counts as "fill", not outline
)

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Src).Path)
$out = New-Object System.Drawing.Bitmap $img.Width, $img.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $img.Height; $y++) {
    for ($x = 0; $x -lt $img.Width; $x++) {
        $c = $img.GetPixel($x, $y)
        if ($c.A -lt 8) { continue }
        $luma = 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
        if ($luma -gt $LumaCut) {
            $out.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($c.A,
                [int]($c.R * $Factor), [int]($c.G * $Factor), [int]($c.B * $Factor)))
        } else {
            $out.SetPixel($x, $y, $c)   # outline and shadow stay put
        }
    }
}

$out.Save((Join-Path (Get-Location) $Dst), [System.Drawing.Imaging.ImageFormat]::Png)
$img.Dispose(); $out.Dispose()
"$Dst written"
