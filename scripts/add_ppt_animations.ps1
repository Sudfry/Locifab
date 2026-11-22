$ErrorActionPreference = "Stop"
$BF = [Reflection.BindingFlags]
$inputPath = "C:\Users\zxyzx\Desktop\LoacalCraft\Document\anim_input.pptx"
$finalPath = "C:\Users\zxyzx\Desktop\LoacalCraft\Document\Locifab-项目方案-初版.pptx"

Get-Process POWERPNT -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Copy-Item $finalPath $inputPath -Force

$app = New-Object -ComObject PowerPoint.Application
try {
    $app.GetType().InvokeMember("Visible", $BF::SetProperty, $null, $app, @($true))
    $presentations = $app.GetType().InvokeMember("Presentations", $BF::GetProperty, $null, $app, @())
    $pres = $presentations.GetType().InvokeMember("Open", $BF::InvokeMethod, $null, $presentations, @($inputPath, $false, $false, $false))

    $slides = $pres.Slides
    for ($si = 1; $si -le $slides.Count; $si++) {
        $slide = $slides.Item($si)
        $shapes = $slide.Shapes
        $items = @()

        for ($i = 1; $i -le $shapes.Count; $i++) {
            $shape = $shapes.Item($i)
            try { $top = [double]$shape.Top } catch { continue }
            try { $left = [double]$shape.Left } catch { continue }
            try { $width = [double]$shape.Width } catch { continue }
            try { $height = [double]$shape.Height } catch { continue }
            if ($top -gt 500) { continue }
            if ($width -lt 40 -and $left -gt 650) { continue }

            $kind = ""
            try { if ($shape.HasTable) { $kind = "table" } } catch {}
            if ($kind -eq "") {
                try {
                    $stype = [int]$shape.Type
                    if ($stype -eq 13) { $kind = "picture" }
                    elseif ($stype -eq 19) { $kind = "table" }
                } catch {}
            }
            if ($kind -eq "") {
                try {
                    if ($shape.HasTextFrame -and $shape.TextFrame.HasText) { $kind = "text" }
                } catch {}
            }
            if ($kind -eq "") { continue }
            $txt = ""
            if ($kind -eq "text") {
                try { $txt = [string]$shape.TextFrame.TextRange.Text } catch {}
            }
            $items += [pscustomobject]@{ Shape = $shape; Top = $top; Left = $left; Kind = $kind; Text = $txt }
        }

        $items = @($items | Sort-Object Top, Left)
        $chosen = @()
        $title = $items | Where-Object { $_.Kind -eq "text" -and $_.Top -lt 210 -and $_.Left -gt 45 -and $_.Shape.Width -gt 250 -and $_.Shape.Height -gt 35 } | Select-Object -First 1
        if ($title) { $chosen += $title }
        $sub = $items | Where-Object { $_.Kind -eq "text" -and $_.Top -ge 105 -and $_.Top -lt 210 -and $_.Left -gt 45 -and $_.Shape.Width -gt 250 -and $_.Shape.Height -lt 35 -and $_ -ne $title } | Select-Object -First 1
        if ($sub) { $chosen += $sub }
        $content = $items | Where-Object { $_.Top -ge 185 -and $_.Top -lt 500 -and $_ -ne $title -and $_ -ne $sub -and ($_.Kind -ne "text" -or $_.Text.Length -gt 2) } | Select-Object -First 3
        $chosen += $content
        if ($chosen.Count -eq 0) { $chosen = $items | Select-Object -First 3 }

        $seq = $slide.TimeLine.MainSequence
        $trigger = 1
        foreach ($item in $chosen) {
            if ($null -eq $item -or $null -eq $item.Shape) { continue }
            try {
                $effect = $seq.AddEffect($item.Shape, 10, 0, $trigger)
                $effect.Timing.Duration = 0.45
                $trigger = 3
            }
            catch {
                Write-Host ("slide {0}: animation failed: {1}" -f $si, $_.Exception.Message) -ForegroundColor Yellow
            }
        }

        try {
            $slide.SlideShowTransition.EntryEffect = 1793
            $slide.SlideShowTransition.Duration = 0.5
        } catch {}
        Write-Host ("slide {0}: {1} animations" -f $si, $seq.Count)
    }

    $pres.Save()
    $pres.Close()
}
finally {
    try { $app.Quit() } catch {}
}

Copy-Item $inputPath $finalPath -Force
Remove-Item $inputPath -Force -ErrorAction SilentlyContinue
Write-Host "animations saved" -ForegroundColor Green
