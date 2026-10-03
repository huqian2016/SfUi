<#
.SYNOPSIS
    Converts a square PNG logo into a multi-size Windows .ico file.
.DESCRIPTION
    Default source : packaging\Assets\Square150x150Logo.png
    Default output : src\SfUi.App\SfUi.ico  (embedded via <ApplicationIcon> + WPF Resource)
    Frames are written as uncompressed 32bpp DIB (BMP) for maximum compatibility
    with the .NET SDK icon embedding and older icon parsers.
    Sizes: 16, 24, 32, 48, 64, 128, 256.
    Tip: use a 512px or larger source PNG for the best-looking 128/256 frames.
.NOTE
    ASCII-only on purpose (Windows PowerShell 5.1 reads BOM-less UTF-8 as ANSI).
.EXAMPLE
    .\New-Icon.ps1
    .\New-Icon.ps1 -SourcePng C:\art\logo-1024.png -OutIco C:\out\app.ico
#>
[CmdletBinding()]
param(
    [string]$SourcePng = (Join-Path $PSScriptRoot 'Assets\Square150x150Logo.png'),
    [string]$OutIco = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\SfUi.App\SfUi.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-DibFrame {
    param([System.Drawing.Image]$Src, [int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($Src, 0, 0, $Size, $Size)
    $g.Dispose()

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($Size * $Size * 4)
    $row = New-Object byte[] $stride
    for ($y = 0; $y -lt $Size; $y++) {
        # DIB pixel rows are stored bottom-up
        $srcRow = $Size - 1 - $y
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + ($srcRow * $stride)), $row, 0, $stride)
        [System.Array]::Copy($row, 0, $pixels, $y * $Size * 4, $Size * 4)
    }
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $maskRowBytes = [int]([math]::Ceiling($Size / 32.0)) * 4   # 1bpp AND mask, padded to 4 bytes
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40)                    # BITMAPINFOHEADER.biSize
    $bw.Write([int32]$Size)                  # biWidth
    $bw.Write([int32]($Size * 2))            # biHeight (XOR + AND)
    $bw.Write([uint16]1)                     # biPlanes
    $bw.Write([uint16]32)                    # biBitCount
    $bw.Write([uint32]0)                     # biCompression = BI_RGB
    $bw.Write([uint32]($Size * $Size * 4))   # biSizeImage
    $bw.Write([int32]0)                      # biXPelsPerMeter
    $bw.Write([int32]0)                      # biYPelsPerMeter
    $bw.Write([uint32]0)                     # biClrUsed
    $bw.Write([uint32]0)                     # biClrImportant
    $bw.Write($pixels)
    $bw.Write((New-Object byte[] ($maskRowBytes * $Size)))
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return ,$bytes
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourcePng).Path)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
try {
    foreach ($s in $sizes) { $frames.Add((New-DibFrame -Src $source -Size $s)) }
} finally {
    $source.Dispose()
}

$outPath = $OutIco
if (-not [System.IO.Path]::IsPathRooted($outPath)) {
    $outPath = Join-Path (Split-Path $PSScriptRoot -Parent) $outPath
}
$outPath = [System.IO.Path]::GetFullPath($outPath)
$dir = Split-Path $outPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)                 # reserved
    $bw.Write([uint16]1)                 # type = icon
    $bw.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $wh = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $len = $frames[$i].Length
        $bw.Write($wh)                   # width (0 = 256)
        $bw.Write($wh)                   # height (0 = 256)
        $bw.Write([byte]0)               # color count
        $bw.Write([byte]0)               # reserved
        $bw.Write([uint16]1)             # planes
        $bw.Write([uint16]32)            # bit count
        $bw.Write([uint32]$len)          # bytes in resource
        $bw.Write([uint32]$offset)       # image offset
        $offset += $len
    }
    foreach ($f in $frames) { $bw.Write($f) }
} finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host ("ICO written: {0} ({1:N0} bytes, sizes: {2})" -f $outPath, (Get-Item $outPath).Length, ($sizes -join '/'))
