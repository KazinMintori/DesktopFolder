param(
    [string]$IcoPath = (Join-Path $PSScriptRoot "DesktopFolders.ico"),
    [string]$PngPath = (Join-Path $PSScriptRoot "DesktopFolders.png")
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([System.Drawing.RectangleF]$Rectangle, [float]$Radius) {
    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Rectangle.Left, $Rectangle.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.Left, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$bitmap = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$tabPath = New-RoundedPath ([System.Drawing.RectangleF]::new(34, 28, 104, 78)) 22
$bodyPath = New-RoundedPath ([System.Drawing.RectangleF]::new(18, 62, 220, 174)) 30
$tabBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 51, 65, 85))
$bodyBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 41, 59))
$edgePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 148, 163, 184), 5)
$graphics.FillPath($tabBrush, $tabPath)
$graphics.FillPath($bodyBrush, $bodyPath)
$graphics.DrawPath($edgePen, $bodyPath)

$tileColors = @(
    [System.Drawing.Color]::FromArgb(255, 59, 130, 246),
    [System.Drawing.Color]::FromArgb(255, 16, 185, 129),
    [System.Drawing.Color]::FromArgb(255, 245, 158, 11),
    [System.Drawing.Color]::FromArgb(255, 139, 92, 246)
)
$tilePositions = @(
    [System.Drawing.RectangleF]::new(48, 88, 64, 58),
    [System.Drawing.RectangleF]::new(128, 88, 64, 58),
    [System.Drawing.RectangleF]::new(48, 160, 64, 58),
    [System.Drawing.RectangleF]::new(128, 160, 64, 58)
)
$whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(245, 248, 250, 252), 5)
$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 248, 250, 252))
$whitePen.StartCap = $whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

for ($index = 0; $index -lt 4; $index++) {
    $tilePath = New-RoundedPath $tilePositions[$index] 13
    $tileBrush = New-Object System.Drawing.SolidBrush($tileColors[$index])
    $graphics.FillPath($tileBrush, $tilePath)
    $tileBrush.Dispose()
    $tilePath.Dispose()
}

$graphics.DrawRectangle($whitePen, 64, 103, 25, 20)
$graphics.DrawRectangle($whitePen, 71, 110, 25, 20)
$graphics.FillEllipse($whiteBrush, 146, 104, 28, 28)
$triangle = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(68, 174),
    [System.Drawing.PointF]::new(68, 204),
    [System.Drawing.PointF]::new(94, 189)
)
$graphics.FillPolygon($whiteBrush, $triangle)
$graphics.DrawLine($whitePen, 146, 177, 174, 177)
$graphics.DrawLine($whitePen, 146, 190, 174, 190)
$graphics.DrawLine($whitePen, 146, 203, 174, 203)

$bitmap.Save($PngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = [System.IO.File]::ReadAllBytes($PngPath)
$stream = New-Object System.IO.FileStream($IcoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([int]$pngBytes.Length)
$writer.Write([int]22)
$writer.Write($pngBytes)
$writer.Dispose()
$stream.Dispose()

$whiteBrush.Dispose()
$whitePen.Dispose()
$edgePen.Dispose()
$bodyBrush.Dispose()
$tabBrush.Dispose()
$bodyPath.Dispose()
$tabPath.Dispose()
$graphics.Dispose()
$bitmap.Dispose()

Get-Item -LiteralPath $IcoPath, $PngPath | Select-Object FullName, Length, LastWriteTime