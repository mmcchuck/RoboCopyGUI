# tools/make-icon.ps1 — generates RoboCopyGUI.ico with the robot-with-paper design.
# Run automatically by build.cmd if RoboCopyGUI.ico is missing or older than icon-draw.ps1.

[CmdletBinding()]
param(
    [string]$Out
)

if (-not $Out) {
    $Out = Join-Path (Split-Path -Parent $PSScriptRoot) 'RoboCopyGUI.ico'
}

. (Join-Path $PSScriptRoot 'icon-draw.ps1')

function Write-Ico {
    param([string]$path, $bitmaps)

    $entries = @()
    foreach ($bmp in $bitmaps) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $entries += @{ Bitmap = $bmp; Data = $ms.ToArray() }
    }

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    try {
        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$entries.Count)

        $offset = 6 + (16 * $entries.Count)

        foreach ($e in $entries) {
            $w = $e.Bitmap.Width
            $h = $e.Bitmap.Height
            $ww = if ($w -ge 256) { 0 } else { $w }
            $hh = if ($h -ge 256) { 0 } else { $h }
            $bw.Write([byte]$ww)
            $bw.Write([byte]$hh)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$e.Data.Length)
            $bw.Write([uint32]$offset)
            $offset += $e.Data.Length
        }

        foreach ($e in $entries) {
            $bw.Write($e.Data)
        }
    }
    finally {
        $bw.Close()
        $fs.Close()
    }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$bitmaps = @()
foreach ($sz in $sizes) {
    $bitmaps += (Draw-RobotIcon -size $sz)
}

Write-Ico -path $Out -bitmaps $bitmaps

foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host ("Wrote {0} ({1} sizes: {2})" -f $Out, $sizes.Count, ($sizes -join ', '))
