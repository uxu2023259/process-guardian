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
    $baseScale = $size / 256.0
    $iconScale = 1.14
    $combinedScale = $baseScale * $iconScale
    $offsetX = -17.92 * $baseScale
    $offsetY = -18.34 * $baseScale
    $transform = [Drawing.Drawing2D.Matrix]::new(
        [single]$combinedScale, [single]0, [single]0, [single]$combinedScale,
        [single]$offsetX, [single]$offsetY)
    $graphics.Transform = $transform
    $transform.Dispose()

    $shield = [Drawing.Drawing2D.GraphicsPath]::new()
    $shield.AddBezier([Drawing.PointF]::new(128, 26), [Drawing.PointF]::new(156, 36), [Drawing.PointF]::new(185, 46), [Drawing.PointF]::new(211, 55))
    $shield.AddLine([Drawing.PointF]::new(211, 55), [Drawing.PointF]::new(207, 132))
    $shield.AddBezier([Drawing.PointF]::new(207, 132), [Drawing.PointF]::new(204, 179), [Drawing.PointF]::new(169, 215), [Drawing.PointF]::new(128, 236))
    $shield.AddBezier([Drawing.PointF]::new(128, 236), [Drawing.PointF]::new(87, 215), [Drawing.PointF]::new(52, 179), [Drawing.PointF]::new(49, 132))
    $shield.AddLine([Drawing.PointF]::new(49, 132), [Drawing.PointF]::new(45, 55))
    $shield.AddBezier([Drawing.PointF]::new(45, 55), [Drawing.PointF]::new(71, 46), [Drawing.PointF]::new(100, 36), [Drawing.PointF]::new(128, 26))
    $shield.CloseFigure()
    $shieldBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.PointF]::new(128, 26), [Drawing.PointF]::new(128, 236),
        [Drawing.Color]::FromArgb(58, 220, 198), [Drawing.Color]::FromArgb(26, 174, 190))
    $graphics.FillPath($shieldBrush, $shield)
    $outlinePen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(19, 44, 62), 4)
    $outlinePen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($outlinePen, $shield)

    $check = [Drawing.Drawing2D.GraphicsPath]::new()
    $check.AddLine([Drawing.PointF]::new(86, 128), [Drawing.PointF]::new(113, 155))
    $check.AddLine([Drawing.PointF]::new(113, 155), [Drawing.PointF]::new(171, 97))
    $checkPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(19, 44, 62), 17)
    $checkPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $checkPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $checkPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($checkPen, $check)

    $pngStream = [IO.MemoryStream]::new()
    $bitmap.Save($pngStream, [Drawing.Imaging.ImageFormat]::Png)
    $frames.Add($pngStream.ToArray())

    $pngStream.Dispose()
    $checkPen.Dispose()
    $check.Dispose()
    $outlinePen.Dispose()
    $shieldBrush.Dispose()
    $shield.Dispose()
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
