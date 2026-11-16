# Generates the app icons from assets\logo.svg's shapes, rendered as vectors at every size (sharp, no downscaling):
#   OnRecord\Assets\onrecord.ico       app icon (exe, taskbar, Alt+Tab, installer)  16..256
#   OnRecord\Assets\onrecord-tray.ico  simplified tray icon: orange bubble + white quotes, no tile
#   OnRecord\Assets\logo-64.png        About line in settings
#   installer\logo-*.png               installer artwork
# Run from the code folder:  powershell -STA -File tools\make-icon.ps1 [-PreviewDir <folder>]
# If you change assets\logo.svg, mirror the shapes below (they are copied 1:1 from the SVG, viewBox 0 0 100 100).
param([string]$PreviewDir)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Split-Path $PSScriptRoot
$assets = Join-Path $root "OnRecord\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function Brush($hex, $opacity = 1.0) { $b = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Opacity = $opacity; $b.Freeze(); $b }
function Geo($data) { [Windows.Media.Geometry]::Parse($data) }
$bubble = 'M34 20 H66 A16 16 0 0 1 82 36 V54 A16 16 0 0 1 66 70 H44 L28 84 L31 70 A16 16 0 0 1 18 54 V36 A16 16 0 0 1 34 20 Z'
$quotes = 'M0,0 L40,0 L40,34 L24,64 L9,64 L20,38 L0,38 Z M52,0 L92,0 L92,34 L76,64 L61,64 L72,38 L52,38 Z'

function Draw-Full($dc) {
    $dc.DrawRoundedRectangle((Brush '#141416'), $null, (New-Object Windows.Rect 0,0,100,100), 23, 23)
    $pen = New-Object Windows.Media.Pen (Brush '#ffffff' 0.12), 1.5
    $dc.DrawRoundedRectangle($null, $pen, (New-Object Windows.Rect 0.75,0.75,98.5,98.5), 22.3, 22.3)
    $pen = New-Object Windows.Media.Pen (Brush '#FF8A3D'), 4; $pen.LineJoin = 'Round'
    $dc.DrawGeometry((Brush '#26211f'), $pen, (Geo $bubble))
    $dc.PushTransform((New-Object Windows.Media.MatrixTransform 0.413,0,0,0.413,31,32))
    $dc.DrawGeometry((Brush '#ffffff'), $null, (Geo $quotes))
    $dc.Pop()
}

# Tray: no tile; bubble filled orange so it reads at 16px on light and dark taskbars. Cropped to the bubble.
function Draw-Tray($dc) {
    $dc.PushTransform((New-Object Windows.Media.MatrixTransform 1.4286,0,0,1.4286,-22.86,-24.29)) # view ~16..86 x 17..87
    $pen = New-Object Windows.Media.Pen (Brush '#FF8A3D'), 4; $pen.LineJoin = 'Round'
    $dc.DrawGeometry((Brush '#FF8A3D'), $pen, (Geo $bubble))
    $dc.PushTransform((New-Object Windows.Media.MatrixTransform 0.413,0,0,0.413,31,32))
    $dc.DrawGeometry((Brush '#ffffff'), $null, (Geo $quotes))
    $dc.Pop(); $dc.Pop()
}

function Render($drawFn, [int]$size) {
    $v = New-Object Windows.Media.DrawingVisual
    $dc = $v.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 100.0), ($size / 100.0)))
    & $drawFn $dc
    $dc.Pop(); $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($v); $bmp
}
function PngBytes($bmp) { $e = New-Object Windows.Media.Imaging.PngBitmapEncoder; $e.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp)); $ms = New-Object IO.MemoryStream; $e.Save($ms); return ,$ms.ToArray() }
function SavePng($bmp, $path) { [IO.File]::WriteAllBytes($path, (PngBytes $bmp)) }

function Write-Ico($drawFn, [int[]]$sizes, $path) {
    $imgs = @(); foreach ($s in $sizes) { $imgs += ,(PngBytes (Render $drawFn $s)) }
    $fs = [IO.File]::Create($path); $w = New-Object IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$imgs[$i].Length); $w.Write([uint32]$offset)
        $offset += $imgs[$i].Length
    }
    foreach ($img in $imgs) { $w.Write($img) }
    $w.Close()
}

Write-Ico ${function:Draw-Full} @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256) (Join-Path $assets "onrecord.ico")
Write-Ico ${function:Draw-Tray} @(16, 20, 24, 32, 40, 48, 64) (Join-Path $assets "onrecord-tray.ico")
SavePng (Render ${function:Draw-Full} 64) (Join-Path $assets "logo-64.png")

$inst = Join-Path $root "installer"; New-Item -ItemType Directory -Force $inst | Out-Null
SavePng (Render ${function:Draw-Full} 55) (Join-Path $inst "logo-55.png")   # Inno Setup small wizard image
SavePng (Render ${function:Draw-Full} 110) (Join-Path $inst "logo-110.png") # ... at 200% DPI

if ($PreviewDir) {
    foreach ($s in 16, 24, 32, 48) {
        SavePng (Render ${function:Draw-Full} $s) (Join-Path $PreviewDir "icon-full-$s.png")
        SavePng (Render ${function:Draw-Tray} $s) (Join-Path $PreviewDir "icon-tray-$s.png")
    }
}
"icons written to $assets"
