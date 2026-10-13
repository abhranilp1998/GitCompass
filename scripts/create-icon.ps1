$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$workspace = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $workspace 'assets\branding'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$images = @()
foreach ($size in @(16, 32, 48, 64, 128, 256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $scale = $size / 64.0
    $graphics.ScaleTransform($scale, $scale)
    $blue = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#1769E0'))
    $pale = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#91C5FF'))
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, 2.5)
    $centerPen = [Drawing.Pen]::new([Drawing.Color]::White, 1.5)
    $rounded = [Drawing.Drawing2D.GraphicsPath]::new()
    $rounded.AddArc(2, 2, 28, 28, 180, 90)
    $rounded.AddArc(34, 2, 28, 28, 270, 90)
    $rounded.AddArc(34, 34, 28, 28, 0, 90)
    $rounded.AddArc(2, 34, 28, 28, 90, 90)
    $rounded.CloseFigure()
    $graphics.FillPath($blue, $rounded)
    $graphics.DrawEllipse($pen, 10, 10, 44, 44)
    $graphics.FillPolygon([Drawing.Brushes]::White, [Drawing.PointF[]]@([Drawing.PointF]::new(46,18), [Drawing.PointF]::new(36,36), [Drawing.PointF]::new(28,28)))
    $graphics.FillPolygon($pale, [Drawing.PointF[]]@([Drawing.PointF]::new(18,46), [Drawing.PointF]::new(28,28), [Drawing.PointF]::new(36,36)))
    $graphics.FillEllipse($blue, 29, 29, 6, 6)
    $graphics.DrawEllipse($centerPen, 29, 29, 6, 6)
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    if ($size -eq 256) { $bitmap.Save((Join-Path $folder 'GitCompass.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $rounded.Dispose(); $blue.Dispose(); $pale.Dispose(); $pen.Dispose(); $centerPen.Dispose()
}
$file = [IO.File]::Create((Join-Path $folder 'GitCompass.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($item in $images) {
        $dimension = if ($item.Size -eq 256) { 0 } else { $item.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$item.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $item.Bytes.Length
    }
    foreach ($item in $images) { $writer.Write([byte[]]$item.Bytes) }
}
finally { $writer.Dispose(); $file.Dispose() }
