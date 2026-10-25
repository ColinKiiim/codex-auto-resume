$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\app.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$encodedImages = @()
foreach ($side in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($side, $side)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $navy = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(22, 36, 64))
    $ring = [Drawing.Pen]::new([Drawing.Color]::FromArgb(41, 197, 220), [single]($side * 0.075))
    $graphics.FillEllipse($navy, [single]($side * .035), [single]($side * .035), [single]($side * .93), [single]($side * .93))
    $graphics.DrawArc($ring, [single]($side * .14), [single]($side * .14), [single]($side * .72), [single]($side * .72), [single]45, [single]285)
    $triangle = [Drawing.PointF[]]@([Drawing.PointF]::new($side * .40, $side * .28), [Drawing.PointF]::new($side * .72, $side * .50), [Drawing.PointF]::new($side * .40, $side * .72))
    $graphics.FillPolygon([Drawing.Brushes]::White, $triangle)
    $memory = [IO.MemoryStream]::new()
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $encodedImages += ,$memory.ToArray()
    $memory.Dispose(); $ring.Dispose(); $navy.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$encodedImages[$index].Length); $writer.Write([uint32]$offset)
        $offset += $encodedImages[$index].Length
    }
    foreach ($encoded in $encodedImages) { $writer.Write([byte[]]$encoded) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output ('Generated icon: ' + $iconPath)
