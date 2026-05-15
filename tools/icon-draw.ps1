# Shared drawing routine for the RoboCopyGUI app icon.
# Dot-source this file: . (Join-Path $PSScriptRoot 'icon-draw.ps1')

Add-Type -AssemblyName System.Drawing
[System.Reflection.Assembly]::LoadWithPartialName('System.Drawing.Drawing2D') | Out-Null

function New-RoundedRectPath {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h))
        return $path
    }
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $path.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Color {
    param([int]$r, [int]$g, [int]$b, [int]$a = 255)
    return [System.Drawing.Color]::FromArgb($a, $r, $g, $b)
}

function Draw-RobotIcon {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $u = [single]($size / 32.0)
    $detail = $size -ge 32

    $teal     = (New-Color 0   188 212)
    $tealDark = (New-Object System.Drawing.Pen ((New-Color 0 96 100), ([Math]::Max(1.0, $u * 0.9))))
    $tealDark.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $eyeYel   = (New-Color 255 235 59)
    $orange   = (New-Color 255 87  34)
    $paper    = (New-Color 255 255 255)
    $fold     = (New-Color 176 190 197)
    $lineCol  = (New-Object System.Drawing.Pen ((New-Color 96 125 139), ([Math]::Max(1.0, $u * 0.55))))

    $tealBrush   = New-Object System.Drawing.SolidBrush $teal
    $eyeBrush    = New-Object System.Drawing.SolidBrush $eyeYel
    $orangeBrush = New-Object System.Drawing.SolidBrush $orange
    $paperBrush  = New-Object System.Drawing.SolidBrush $paper
    $foldBrush   = New-Object System.Drawing.SolidBrush $fold

    if ($detail) {
        $antX = [single]($u * 16)
        $antTop = [single]($u * 3.0)
        $antBottom = [single]($u * 8.0)
        $antPen = New-Object System.Drawing.Pen ((New-Color 0 96 100), ([Math]::Max(1.0, $u * 1.1)))
        $antPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $antPen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($antPen, $antX, $antBottom, $antX, $antTop)
        $ballR = [single]($u * 2.0)
        $g.FillEllipse($orangeBrush, ($antX - $ballR), ($antTop - $ballR), ($ballR * 2), ($ballR * 2))
        $g.DrawEllipse($tealDark, ($antX - $ballR), ($antTop - $ballR), ($ballR * 2), ($ballR * 2))
        $antPen.Dispose()
    }

    $headPath = New-RoundedRectPath ($u * 4.5) ($u * 7.0) ($u * 23.0) ($u * 16.0) ($u * 4.0)
    $g.FillPath($tealBrush, $headPath)
    $g.DrawPath($tealDark, $headPath)

    if ($detail) {
        $earR = [single]($u * 1.5)
        $earY = [single]($u * 13)
        $g.FillEllipse($tealBrush, ($u * 3.0), $earY, ($earR * 2), ($earR * 2))
        $g.DrawEllipse($tealDark, ($u * 3.0), $earY, ($earR * 2), ($earR * 2))
        $g.FillEllipse($tealBrush, ($u * 27.0), $earY, ($earR * 2), ($earR * 2))
        $g.DrawEllipse($tealDark, ($u * 27.0), $earY, ($earR * 2), ($earR * 2))
    }

    $eyeR = [single]($u * 2.6)
    $eyeY = [single]($u * 11.0)
    $leftEyeX  = [single]($u * 9.5)
    $rightEyeX = [single]($u * 19.0)
    $g.FillEllipse($eyeBrush, $leftEyeX,  $eyeY, ($eyeR * 2), ($eyeR * 2))
    $g.FillEllipse($eyeBrush, $rightEyeX, $eyeY, ($eyeR * 2), ($eyeR * 2))
    $g.DrawEllipse($tealDark, $leftEyeX,  $eyeY, ($eyeR * 2), ($eyeR * 2))
    $g.DrawEllipse($tealDark, $rightEyeX, $eyeY, ($eyeR * 2), ($eyeR * 2))
    $pupilR = [single]($u * 1.05)
    $g.FillEllipse([System.Drawing.Brushes]::Black, ($leftEyeX  + $eyeR - $pupilR), ($eyeY + $eyeR - $pupilR + $u * 0.2), ($pupilR * 2), ($pupilR * 2))
    $g.FillEllipse([System.Drawing.Brushes]::Black, ($rightEyeX + $eyeR - $pupilR), ($eyeY + $eyeR - $pupilR + $u * 0.2), ($pupilR * 2), ($pupilR * 2))

    $g.TranslateTransform(($u * 16), ($u * 23.5))
    if ($detail) { $g.RotateTransform(-6) }

    $docW = [single]($u * 18.0)
    $docH = [single]($u * 12.0)
    $docX = -$docW / 2
    $docY = -$docH / 2
    $foldSize = [single]($u * 4.5)

    $docPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $docX, $docY),
        (New-Object System.Drawing.PointF ($docX + $docW - $foldSize), $docY),
        (New-Object System.Drawing.PointF ($docX + $docW), ($docY + $foldSize)),
        (New-Object System.Drawing.PointF ($docX + $docW), ($docY + $docH)),
        (New-Object System.Drawing.PointF $docX, ($docY + $docH))
    )
    $docPath.AddPolygon($pts)
    $g.FillPath($paperBrush, $docPath)
    $g.DrawPath($tealDark, $docPath)

    $foldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $foldPts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($docX + $docW - $foldSize), $docY),
        (New-Object System.Drawing.PointF ($docX + $docW - $foldSize), ($docY + $foldSize)),
        (New-Object System.Drawing.PointF ($docX + $docW), ($docY + $foldSize))
    )
    $foldPath.AddPolygon($foldPts)
    $g.FillPath($foldBrush, $foldPath)
    $g.DrawPath($tealDark, $foldPath)

    if ($detail) {
        $margin = $u * 2.0
        $lineXStart = $docX + $margin
        $lineXEndShort = $docX + $docW - $margin - $foldSize
        $lineXEndFull  = $docX + $docW - $margin
        $g.DrawLine($lineCol, $lineXStart, ($docY + $u * 3.5), $lineXEndShort, ($docY + $u * 3.5))
        $g.DrawLine($lineCol, $lineXStart, ($docY + $u * 6.0), $lineXEndFull,  ($docY + $u * 6.0))
        $g.DrawLine($lineCol, $lineXStart, ($docY + $u * 8.5), $lineXEndFull,  ($docY + $u * 8.5))
    }

    $g.ResetTransform()
    $g.Dispose()

    $tealDark.Dispose()
    $lineCol.Dispose()
    $tealBrush.Dispose()
    $eyeBrush.Dispose()
    $orangeBrush.Dispose()
    $paperBrush.Dispose()
    $foldBrush.Dispose()

    return $bmp
}
