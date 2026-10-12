# Draws EasyEngine.ico (the launcher's green hexagon on the dark app background) at 16-256 px.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File tools\Make-Icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'EasyEngine.ico'
$sizes = 256, 64, 48, 32, 24, 16

function Draw-Png([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    # rounded dark tile
    $r = [Math]::Max(2, $s * 0.22); $d = $r * 2; $m = [Math]::Max(0, $s * 0.02); $w = $s - 2 * $m
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc($m, $m, $d, $d, 180, 90); $tile.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $tile.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $tile.AddArc($m, $m + $w - $d, $d, $d, 90, 90); $tile.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 28, 34))), $tile)
    # pointy-top hexagon outline (the launcher's logo)
    $cx = $s / 2; $cy = $s / 2; $hr = $s * 0.30
    $pts = 0..5 | ForEach-Object { $a = [Math]::PI / 180 * (60 * $_ - 90); New-Object System.Drawing.PointF ([single]($cx + $hr * [Math]::Cos($a))), ([single]($cy + $hr * [Math]::Sin($a))) }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 24, 185, 138)), ([single][Math]::Max(1.5, $s * 0.085))
    $pen.LineJoin = 'Round'
    $g.DrawPolygon($pen, [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}

# ICO = header + one directory entry per size + the PNGs
$pngs = $sizes | ForEach-Object { ,(Draw-Png $_) }
$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
$bw.Close()
"wrote $out"
