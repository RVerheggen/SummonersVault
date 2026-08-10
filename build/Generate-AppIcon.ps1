param(
    [string]$OutputPath = "src\SummonersVault.App\Assets\AppIcon.ico"
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1, [int]($size * 0.06))
    $borderWidth = [Math]::Max(1, [single]($size * 0.035))
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0D1016'))
    $border = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#313746'), $borderWidth)
    $gold = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#D0A54F'))
    $primary = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#B88A3D'))

    $graphics.FillRectangle($background, $margin, $margin, $size - (2 * $margin), $size - (2 * $margin))
    $graphics.DrawRectangle($border, $margin, $margin, $size - (2 * $margin), $size - (2 * $margin))

    $center = [single]($size / 2)
    $outer = [single]($size * 0.36)
    $middle = [single]($size * 0.23)
    $inner = [single]($size * 0.12)
    $graphics.FillPolygon($gold, @(
        [System.Drawing.PointF]::new($center, $center - $outer),
        [System.Drawing.PointF]::new($center + $outer, $center),
        [System.Drawing.PointF]::new($center, $center + $outer),
        [System.Drawing.PointF]::new($center - $outer, $center)))
    $graphics.FillPolygon($background, @(
        [System.Drawing.PointF]::new($center, $center - $middle),
        [System.Drawing.PointF]::new($center + $middle, $center),
        [System.Drawing.PointF]::new($center, $center + $middle),
        [System.Drawing.PointF]::new($center - $middle, $center)))
    $graphics.FillPolygon($primary, @(
        [System.Drawing.PointF]::new($center, $center - $inner),
        [System.Drawing.PointF]::new($center + $inner, $center),
        [System.Drawing.PointF]::new($center, $center + $inner),
        [System.Drawing.PointF]::new($center - $inner, $center)))

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose()
    $background.Dispose()
    $border.Dispose()
    $gold.Dispose()
    $primary.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$images.Count)
$offset = 6 + (16 * $images.Count)

for ($index = 0; $index -lt $images.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}

foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()
$file.Dispose()
