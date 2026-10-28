param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'app.ico'),
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)
    $samples = if ($Size -lt 64) { 4 } else { 2 }
    $large = [System.Drawing.Bitmap]::new($Size * $samples, $Size * $samples, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [System.Drawing.Graphics]::FromImage($large)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(17, 19, 22))
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(31, 35, 40))
    $tile = New-RoundedPath 16 16 224 224 46
    $document = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(48, 53, 60), [single][Math]::Max(2, 0.5 * 256 / $Size))
    $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(119, 134, 139), [single][Math]::Max(6, 0.8 * 256 / $Size))
    $accent = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(133, 199, 177), [single][Math]::Max(8, 0.95 * 256 / $Size))
    foreach ($pen in @($outline, $accent)) {
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    }
    try {
        $canvas.Clear([System.Drawing.Color]::Transparent)
        $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $canvas.ScaleTransform([single]($Size * $samples / 256), [single]($Size * $samples / 256))
        $canvas.FillPath($background, $tile)
        $canvas.DrawPath($border, $tile)

        $document.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(76, 52), [System.Drawing.PointF]::new(151, 52),
            [System.Drawing.PointF]::new(186, 87), [System.Drawing.PointF]::new(186, 204),
            [System.Drawing.PointF]::new(76, 204)))
        $canvas.FillPath($paper, $document)
        $canvas.DrawPath($outline, $document)
        $canvas.DrawLines($outline, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(151, 52), [System.Drawing.PointF]::new(151, 87),
            [System.Drawing.PointF]::new(186, 87)))

        # An uppercase A identifies the name-cleanup action without raster text/fonts.
        $canvas.DrawLines($accent, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(99, 162), [System.Drawing.PointF]::new(123, 108),
            [System.Drawing.PointF]::new(148, 162)))
        $canvas.DrawLine($accent, 108, 142, 139, 142)

        # A short arrow breaks out of the document to indicate renaming/moving.
        $canvas.DrawLine($accent, 129, 186, 205, 186)
        $canvas.DrawLines($accent, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(187, 169), [System.Drawing.PointF]::new(205, 186),
            [System.Drawing.PointF]::new(187, 203)))
    }
    finally {
        $accent.Dispose(); $outline.Dispose(); $border.Dispose()
        $document.Dispose(); $tile.Dispose(); $paper.Dispose(); $background.Dispose(); $canvas.Dispose()
    }

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $resizer = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $resizer.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $resizer.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $resizer.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $resizer.DrawImage($large, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
    }
    finally { $resizer.Dispose(); $large.Dispose() }
    return $bitmap
}

$frames = [System.Collections.Generic.List[object]]::new()
$bitmaps = [System.Collections.Generic.Dictionary[int, System.Drawing.Bitmap]]::new()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = New-IconBitmap $size
        $bitmaps.Add($size, $bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add([pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() })
        }
        finally { $stream.Dispose() }
    }

    $file = [System.IO.File]::Create($OutputPath)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    }
    finally { $writer.Dispose() }

    if ($PreviewPath) {
        $preview = [System.Drawing.Bitmap]::new(640, 320)
        $previewCanvas = [System.Drawing.Graphics]::FromImage($preview)
        $previewFont = [System.Drawing.Font]::new('Segoe UI', 10)
        $previewBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(148, 157, 168))
        try {
            $previewCanvas.Clear([System.Drawing.Color]::FromArgb(24, 27, 31))
            $previewCanvas.DrawImageUnscaled($bitmaps[256], 16, 32)
            $previewCanvas.DrawString('Native icon sizes', $previewFont, $previewBrush, 320, 32)
            $x = 320
            foreach ($size in @(16, 24, 32, 48, 64)) {
                $previewCanvas.DrawImageUnscaled($bitmaps[$size], $x, 90)
                $previewCanvas.DrawString([string]$size, $previewFont, $previewBrush, $x, 164)
                $x += $size + 12
            }
            $previewCanvas.DrawString('Filename Normalizer', $previewFont, $previewBrush, 320, 230)
            $preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $previewBrush.Dispose(); $previewFont.Dispose(); $previewCanvas.Dispose(); $preview.Dispose() }
    }
    Write-Output "Generated icon: $OutputPath (16, 24, 32, 48, 64, 128, 256 px)"
}
finally { foreach ($bitmap in $bitmaps.Values) { $bitmap.Dispose() } }
