# Renders the same robot icon at requested sizes to PNG for review.
[CmdletBinding()]
param(
    [int[]]$Sizes = @(256, 64, 32, 16)
)

. (Join-Path $PSScriptRoot 'icon-draw.ps1')

foreach ($sz in $Sizes) {
    $b = Draw-RobotIcon -size $sz
    $out = Join-Path (Split-Path -Parent $PSScriptRoot) ("icon-preview-{0}.png" -f $sz)
    $b.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    Write-Host "Wrote $out"
}
