param([string]$Iteration = 'final', [string]$Shot = '02-active-carry-stack', [string]$Suffix = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$folder = Join-Path $root 'Documentation/UIHudComposition'
$paths = @((Join-Path $folder 'approved-reference.png'), (Join-Path $folder "rejected/$Shot.png"), (Join-Path $folder "$Iteration/$Shot.png"))
$labels = @('APPROVED', 'REJECTED', 'NEW')
$bitmap = [Drawing.Bitmap]::new(858,528)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([Drawing.Color]::FromArgb(18,40,44))
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [Drawing.Font]::new('Arial',11,[Drawing.FontStyle]::Bold)
$format = [Drawing.StringFormat]::new()
$format.Alignment = [Drawing.StringAlignment]::Center
for ($i=0; $i -lt 3; $i++) {
    $source = [Drawing.Image]::FromFile($paths[$i])
    $x = 12+$i*282
    $graphics.DrawString($labels[$i],$font,[Drawing.Brushes]::Ivory,[Drawing.RectangleF]::new($x,10,270,22),$format)
    $graphics.DrawImage($source,[Drawing.Rectangle]::new($x,36,270,480))
    $source.Dispose()
}
$bitmap.Save((Join-Path $folder "APPROVED-REJECTED-NEW-$Iteration$Suffix-thumbnail.png"),[Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose();$bitmap.Dispose();$font.Dispose();$format.Dispose()
# Same composition comparison with the requested top HUD/objective enlarged.
$bitmap = [Drawing.Bitmap]::new(1398,206)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.Clear([Drawing.Color]::FromArgb(18,40,44))
$graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = [Drawing.Font]::new('Arial',12,[Drawing.FontStyle]::Bold)
for ($i=0; $i -lt 3; $i++) {
    $source = [Drawing.Image]::FromFile($paths[$i])
    $x=12+$i*462
    $graphics.DrawString($labels[$i],$font,[Drawing.Brushes]::Ivory,$x,10)
    $graphics.DrawImage($source,[Drawing.Rectangle]::new($x,38,450,150),0,0,$source.Width,[int]($source.Width/3),[Drawing.GraphicsUnit]::Pixel)
    $source.Dispose()
}
$bitmap.Save((Join-Path $folder "APPROVED-REJECTED-NEW-$Iteration$Suffix-hud.png"),[Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose();$bitmap.Dispose();$font.Dispose()
