# 生成 src/BingWallpaper/app.ico（多尺寸，小尺寸用 BMP 帧、大尺寸用 PNG 帧）
# 用法: pwsh tools/make-icon.ps1
[CmdletBinding()]
param()

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$dest = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\BingWallpaper\app.ico'))
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap {
    param([int]$Size)

    $s = [single]$Size
    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角底板 + 蓝色渐变
    $r = [single]($s * 0.22)
    $d = [single]($r * 2)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc([single]($s - $d), 0, $d, $d, 270, 90)
    $path.AddArc([single]($s - $d), [single]($s - $d), $d, $d, 0, 90)
    $path.AddArc(0, [single]($s - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = [System.Drawing.RectangleF]::new(0, 0, $s, $s)
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 13, 71, 161),
        [System.Drawing.Color]::FromArgb(255, 0, 172, 193),
        [single]65)
    $g.FillPath($bg, $path)

    # 太阳
    $sun = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 213, 79))
    $sd = [single]($s * 0.26)
    $g.FillEllipse($sun, [single]($s * 0.60), [single]($s * 0.16), $sd, $sd)

    # 远山
    $back = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(150, 255, 255, 255))
    $g.FillPolygon($back, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([single]($s * 0.02), [single]($s * 0.82)),
            [System.Drawing.PointF]::new([single]($s * 0.36), [single]($s * 0.32)),
            [System.Drawing.PointF]::new([single]($s * 0.72), [single]($s * 0.82))))

    # 近山
    $front = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $g.FillPolygon($front, [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([single]($s * 0.26), [single]($s * 0.86)),
            [System.Drawing.PointF]::new([single]($s * 0.64), [single]($s * 0.42)),
            [System.Drawing.PointF]::new([single]($s * 1.00), [single]($s * 0.86))))

    $g.Dispose()
    return $bmp
}

function Get-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $n = $Bitmap.Width
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)

    # BITMAPINFOHEADER（高度写两倍：XOR 图 + AND 掩码）
    $bw.Write([uint32]40)
    $bw.Write([int32]$n)
    $bw.Write([int32]($n * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($n * $n * 4))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)

    # BGRA 像素，自下而上
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $Bitmap.GetPixel($x, $y)
            $bw.Write([byte]$c.B)
            $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R)
            $bw.Write([byte]$c.A)
        }
    }

    # AND 掩码：每行补齐到 4 字节
    $rowBytes = [int]([Math]::Ceiling($n / 32.0) * 4)
    $bw.Write([byte[]]::new($rowBytes * $n))

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return $bytes
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $ms = [System.IO.MemoryStream]::new()
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return $bytes
}

$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $data = if ($size -ge 128) { Get-PngBytes -Bitmap $bmp } else { Get-DibBytes -Bitmap $bmp }
    $frames += [pscustomobject]@{ Size = $size; Data = $data }
    $bmp.Dispose()
    Write-Host ("  帧 {0}x{0} -> {1} 字节" -f $size, $data.Length)
}

$dir = Split-Path $dest -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$stream = [System.IO.File]::Create($dest)
$writer = [System.IO.BinaryWriter]::new($stream)

$writer.Write([uint16]0)                # reserved
$writer.Write([uint16]1)                # type = icon
$writer.Write([uint16]$frames.Count)    # count

$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dim)           # width
    $writer.Write([byte]$dim)           # height
    $writer.Write([byte]0)              # 调色板数
    $writer.Write([byte]0)              # reserved
    $writer.Write([uint16]1)            # planes
    $writer.Write([uint16]32)           # bpp
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame.Data)
}

$writer.Flush()
$writer.Dispose()
$stream.Dispose()

Write-Host ("已生成 {0}（{1} 字节）" -f $dest, (Get-Item $dest).Length)
