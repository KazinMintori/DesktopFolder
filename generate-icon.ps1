param(
    [string]$SourcePng = (Join-Path $PSScriptRoot "assets\DesktopFolders.png"),
    [string]$IcoPath = (Join-Path $PSScriptRoot "assets\DesktopFolders.ico")
)

if (-not (Test-Path -LiteralPath $SourcePng)) { throw "Không tìm thấy icon PNG nguồn: $SourcePng" }

Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Image]::FromFile($SourcePng)
$bitmap = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.DrawImage($source, 0, 0, 256, 256)

$pngStream = New-Object System.IO.MemoryStream
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$payload = $pngStream.ToArray()
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
$writer.Write([int]$payload.Length)
$writer.Write([int]22)
$writer.Write($payload)

$writer.Dispose()
$stream.Dispose()
$pngStream.Dispose()
$graphics.Dispose()
$bitmap.Dispose()
$source.Dispose()

Get-Item -LiteralPath $SourcePng, $IcoPath | Select-Object FullName, Length, LastWriteTime
