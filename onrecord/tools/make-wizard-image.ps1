# Renders the installer's Welcome/Finish side image (installer\wizard-164.png and wizard-410.png) from the app icon.
# Run from the repository root: powershell -NoProfile -File tools\make-wizard-image.ps1
Add-Type -AssemblyName System.Drawing, PresentationCore, WindowsBase
$ico = Join-Path $PSScriptRoot "..\OnRecord\Assets\onrecord.ico"
$out = Join-Path $PSScriptRoot "..\installer"
foreach ($scale in @(@(164, 314), @(410, 797))) {
    $w = $scale[0]; $h = $scale[1]; $k = $w / 164.0
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $rect = New-Object Drawing.Rectangle 0, 0, $w, $h
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(58, 36, 24)), ([Drawing.Color]::FromArgb(22, 20, 28)), 90.0
    $g.FillRectangle($bg, $rect)
    # System.Drawing can't read the PNG-compressed 256px frame; WPF can.
    $dec = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Resolve-Path $ico).Path, 'None', 'OnLoad')
    $frame = $dec.Frames | Sort-Object PixelWidth -Descending | Select-Object -First 1
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($frame))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); $ms.Position = 0
    $logo = New-Object Drawing.Bitmap $ms; $size = [int](96 * $k)
    $g.DrawImage($logo, [int](($w - $size) / 2), [int](92 * $k), $size, $size)
    $font = New-Object Drawing.Font "Segoe UI Semibold", ([float](15 * $k)), ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
    $small = New-Object Drawing.Font "Segoe UI", ([float](10 * $k)), ([Drawing.FontStyle]::Regular), ([Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'
    $g.DrawString("OnRecord", $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, ([float](200 * $k)), $w, ([float](24 * $k))), $fmt)
    $grey = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(170, 170, 180))
    $g.DrawString("Global Mayhem", $small, $grey, (New-Object Drawing.RectangleF 0, ([float](222 * $k)), $w, ([float](18 * $k))), $fmt)
    $g.Dispose()
    $file = Join-Path $out "wizard-$w.png"
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "wrote $file"
}
