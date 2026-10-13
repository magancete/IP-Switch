<#
.SYNOPSIS
    Generates src/IPSwitch/Resources/favicon.ico from the vector master (src/IPSwitch/Resources/AppIcon.xaml).

.DESCRIPTION
    Windows needs a raster .ico for the executable, the taskbar and the notification area. This script renders
    the WPF drawings of AppIcon.xaml at every standard size and packs them as PNG entries in one .ico:

      - 256, 64, 48 and 32 px from AppIconImage (full detail);
      - 24 and 16 px from AppIconSmallImage (simplified, legible at small sizes).

    Run it after changing AppIcon.xaml and commit the regenerated favicon.ico.

.PARAMETER PreviewFolder
    Optional folder where the rendered PNG of every size is also written, to review them.

.EXAMPLE
    ./tools/Export-Icon.ps1
#>
[CmdletBinding()]
param([string]$PreviewFolder)

$ErrorActionPreference = 'Stop'

# WPF rendering needs an STA thread: relaunch in Windows PowerShell with -STA when needed
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $arguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PreviewFolder) { $arguments += @('-PreviewFolder', $PreviewFolder) }
    & powershell.exe @arguments
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$resources = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\IPSwitch\Resources'
$dictionary = [Windows.Markup.XamlReader]::Parse((Get-Content -Raw -Encoding UTF8 (Join-Path $resources 'AppIcon.xaml')))

function Render-Png([Windows.Media.ImageSource]$image, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($image, (New-Object Windows.Rect(0, 0, $size, $size)))
    $context.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

$sizes = 256, 64, 48, 32, 24, 16
$images = @{}   # Plain hashtable: an [ordered] dictionary treats integer keys as positions
foreach ($size in $sizes) {
    $key = if ($size -ge 32) { 'AppIconImage' } else { 'AppIconSmallImage' }
    $images[$size] = Render-Png $dictionary[$key] $size
    if ($PreviewFolder) {
        New-Item -ItemType Directory -Force $PreviewFolder | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $PreviewFolder "icon-$size.png"), $images[$size])
    }
}

# ICO container: header, one 16-byte directory entry per size, then the PNG data (supported since Windows Vista)
$ico = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($ico)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($size in $sizes) {
    $dimension = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in the ICO format
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$size].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$size].Length
}
foreach ($size in $sizes) { $writer.Write($images[$size]) }
$writer.Flush()

$target = Join-Path $resources 'favicon.ico'
[IO.File]::WriteAllBytes($target, $ico.ToArray())
Write-Host ("{0}: {1:N0} bytes ({2} px)" -f $target, $ico.Length, ($sizes -join ', ')) -ForegroundColor Green
