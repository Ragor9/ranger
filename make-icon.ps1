# Gera ranger.ico (16–256 px): quadrado escuro arredondado com anel de "medidor" verde e ponto central.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'ranger.ico'

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # fundo
    $r = [Math]::Max(3, $s * 0.22); $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 0.01, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 0.01, $s - $d - 0.01, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 0.01, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x1E, 0x1F, 0x22))), $path)

    # anel (trilho + arco de nível)
    $w = [Math]::Max(2, $s * 0.13)
    $m = $s * 0.22 + $w / 2
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), $w
    $g.DrawEllipse($track, $rect)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x46, 0xC2, 0x85)), $w
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawArc($pen, $rect, -90, 270)

    # ponto central
    $c = [Math]::Max(2, $s * 0.14)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF2, 0xF3, 0xF5))), ($s - $c) / 2, ($s - $c) / 2, $c, $c)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) { ,(New-Frame $s) }

$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$frames[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Close()
"ok: $out"
