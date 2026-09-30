# Render the existing play-mark geometry at each Windows/browser icon size.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path $PSScriptRoot -Parent
$assetDirectory = Join-Path $projectRoot 'src\CinePresence.App\Assets'
$browserDirectory = Join-Path $projectRoot 'browser-companion\icons'
New-Item -ItemType Directory -Force $browserDirectory | Out-Null
$images = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size / 64.0),($size / 64.0))
    $purple = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(181,162,255))
    $ink = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(23,17,36))
    $graphics.FillEllipse($purple,2,2,60,60)
    $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(26,17),[System.Drawing.PointF]::new(26,47),[System.Drawing.PointF]::new(46,32))
    $graphics.FillPolygon($ink,$points)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{Size=$size; Bytes=$stream.ToArray()}
    if ($size -in @(16,32,48,128)) { $bitmap.Save((Join-Path $browserDirectory ($size.ToString()+'.png')),[System.Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $purple.Dispose(); $ink.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetDirectory 'CinePresence.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
