param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\Switcher.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $inset = [Math]::Max(1, $size * 0.035)
        $circle = [System.Drawing.RectangleF]::new($inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $circle,
            [System.Drawing.Color]::FromArgb(24, 32, 43),
            [System.Drawing.Color]::FromArgb(15, 118, 110),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        try { $graphics.FillEllipse($background, $circle) } finally { $background.Dispose() }

        $penWidth = [Math]::Max(1.4, $size * 0.07)
        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(226, 232, 240), $penWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        try {
            $p = { param([double]$value) [single]($value * $size / 32.0) }
            $graphics.DrawLine($pen, (& $p 9), (& $p 12), (& $p 21), (& $p 12))
            $graphics.DrawLine($pen, (& $p 21), (& $p 12), (& $p 17), (& $p 8))
            $graphics.DrawLine($pen, (& $p 21), (& $p 12), (& $p 17), (& $p 16))
            $graphics.DrawLine($pen, (& $p 23), (& $p 20), (& $p 11), (& $p 20))
            $graphics.DrawLine($pen, (& $p 11), (& $p 20), (& $p 15), (& $p 16))
            $graphics.DrawLine($pen, (& $p 11), (& $p 20), (& $p 15), (& $p 24))
        } finally { $pen.Dispose() }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $stream.Dispose() }
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$output = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally {
    $writer.Dispose()
    $output.Dispose()
}

Write-Output (Resolve-Path $OutputPath)
