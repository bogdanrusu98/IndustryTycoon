param([string]$Iteration = 'meshy-iteration1', [string]$Shot = '02-active-carry-stack')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$folder = Join-Path $root 'Documentation/UIAssetRebuild'
$paths = @((Join-Path $folder 'old.png'), (Join-Path $folder 'approved.png'), (Join-Path $root "Documentation/UIHudComposition/$Iteration/$Shot.png"))
$labels = @('OLD', 'APPROVED REFERENCE', 'NEW')
foreach ($crop in @($false, $true)) {
    $width = if ($crop) { 450 } else { 270 }
    $height = if ($crop) { 160 } else { 480 }
    $bitmap = [Drawing.Bitmap]::new(3 * ($width + 12) + 12, $height + 48)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::FromArgb(18,40,44))
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $font = [Drawing.Font]::new('Arial',11,[Drawing.FontStyle]::Bold)
    $format = [Drawing.StringFormat]::new()
    $format.Alignment = [Drawing.StringAlignment]::Center
    for ($i=0; $i -lt 3; $i++) {
        $source = [Drawing.Image]::FromFile($paths[$i])
        $x = 12 + $i * ($width + 12)
        $graphics.DrawString($labels[$i], $font, [Drawing.Brushes]::Ivory, [Drawing.RectangleF]::new($x,10,$width,22), $format)
        $sourceHeight = if ($crop) { [int]($source.Width * $height / $width) } else { $source.Height }
        $graphics.DrawImage($source, [Drawing.Rectangle]::new($x,36,$width,$height), 0, 0, $source.Width, $sourceHeight, [Drawing.GraphicsUnit]::Pixel)
        $source.Dispose()
    }
    $suffix = if ($crop) { 'hud' } else { 'thumbnail' }
    $bitmap.Save((Join-Path $folder "$Iteration-$Shot-$suffix.png"),[Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose(); $font.Dispose(); $format.Dispose()
}
