# Regenerates src/ReController.App/Assets/reController.ico and logo.png.
# Usage: powershell -ExecutionPolicy Bypass -File tools/generate-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/ReController.App/Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 256.0
    $bg = New-RoundedPath (8*$s) (8*$s) (240*$s) (240*$s) (56*$s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0,0)), ([System.Drawing.PointF]::new($size,$size)), ([System.Drawing.Color]::FromArgb(255,18,24,36)), ([System.Drawing.Color]::FromArgb(255,8,10,15))
    $g.FillPath($brush, $bg)
    $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,61,214,245)), ([single](6*$s))
    $g.DrawPath($rim, $bg)

    # Gamepad silhouette
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier(70*$s,96*$s, 100*$s,84*$s, 156*$s,84*$s, 186*$s,96*$s)
    $body.AddBezier(186*$s,96*$s, 214*$s,106*$s, 230*$s,170*$s, 214*$s,186*$s)
    $body.AddBezier(214*$s,186*$s, 198*$s,200*$s, 176*$s,176*$s, 162*$s,160*$s)
    $body.AddLine(162*$s,160*$s, 94*$s,160*$s)
    $body.AddBezier(94*$s,160*$s, 80*$s,176*$s, 58*$s,200*$s, 42*$s,186*$s)
    $body.AddBezier(42*$s,186*$s, 26*$s,170*$s, 42*$s,106*$s, 70*$s,96*$s)
    $body.CloseFigure()
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,61,214,245))
    $g.FillPath($accent, $body)
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,10,14,20))
    $g.FillEllipse($dark, 74*$s, 112*$s, 30*$s, 30*$s)
    $g.FillEllipse($dark, 152*$s, 112*$s, 30*$s, 30*$s)
    # "re" arrow: circular swap mark between sticks
    $pen = New-Object System.Drawing.Pen $dark, ([single](8*$s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, 112*$s, 104*$s, 32*$s, 32*$s, 200, 250)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $b.Save((Join-Path $assets 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'reController.ico'), $out.ToArray())
Write-Host "Icon written to $assets"
