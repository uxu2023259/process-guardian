Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path $PSScriptRoot '..\Assets'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$outputDirectory = [IO.Path]::GetFullPath($outputDirectory)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)

    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    $tile.AddArc(8, 8, 76, 76, 180, 90)
    $tile.AddArc(172, 8, 76, 76, 270, 90)
    $tile.AddArc(172, 172, 76, 76, 0, 90)
    $tile.AddArc(8, 172, 76, 76, 90, 90)
    $tile.CloseFigure()
    $background = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(19, 34, 54))
    $graphics.FillPath($background, $tile)

    $shield = [Drawing.Drawing2D.GraphicsPath]::new()
    $shieldPoints = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(128, 38), [Drawing.PointF]::new(198, 62),
        [Drawing.PointF]::new(194, 128), [Drawing.PointF]::new(181, 160),
        [Drawing.PointF]::new(156, 184), [Drawing.PointF]::new(128, 202),
        [Drawing.PointF]::new(100, 184), [Drawing.PointF]::new(75, 160),
        [Drawing.PointF]::new(62, 128), [Drawing.PointF]::new(58, 62)
    )
    $shield.AddPolygon($shieldPoints)
    $shieldBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.PointF]::new(128, 38), [Drawing.PointF]::new(128, 202),
        [Drawing.Color]::FromArgb(70, 226, 206), [Drawing.Color]::FromArgb(30, 180, 196))
    $graphics.FillPath($shieldBrush, $shield)

    $check = [Drawing.Drawing2D.GraphicsPath]::new()
    $checkPoints = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(91, 121), [Drawing.PointF]::new(108, 138),
        [Drawing.PointF]::new(158, 88), [Drawing.PointF]::new(175, 105),
        [Drawing.PointF]::new(108, 172), [Drawing.PointF]::new(74, 138)
    )
    $check.AddPolygon($checkPoints)
    $checkBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(19, 44, 62))
    $graphics.FillPath($checkBrush, $check)

    $pngStream = [IO.MemoryStream]::new()
    $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($pngStream.ToArray())

    $pngStream.Dispose()
    $checkBrush.Dispose()
    $check.Dispose()
    $shieldBrush.Dispose()
    $shield.Dispose()
    $background.Dispose()
    $tile.Dispose()
    $graphics.Dispose()

    if ($size -eq 256) {
        $bitmap.Save((Join-Path $outputDirectory 'process-guardian.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    $bitmap.Dispose()
}

$iconPath = Join-Path $outputDirectory 'process-guardian.ico'
$stream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $frame = $frames[$i]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()
$stream.Dispose()
Write-Output "Created $iconPath"
