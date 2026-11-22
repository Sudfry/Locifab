$ErrorActionPreference = "Stop"
$app = New-Object -ComObject PowerPoint.Application
$BF = [Reflection.BindingFlags]
try {
    $app.GetType().InvokeMember("Visible", $BF::SetProperty, $null, $app, @($true))
    $presCol = $app.GetType().InvokeMember("Presentations", $BF::GetProperty, $null, $app, @())
    "PRESENTATIONS=" + ($null -ne $presCol)
    $pres = $presCol.GetType().InvokeMember("Open", $BF::InvokeMethod, $null, $presCol, @(
        "C:\Users\zxyzx\Desktop\LoacalCraft\Document\test_deck.pptx"))
    "SLIDES=" + $pres.GetType().InvokeMember("Slides", $BF::GetProperty, $null, $pres, @()).Count
    $pres.GetType().InvokeMember("Close", $BF::InvokeMethod, $null, $pres, @())
}
finally {
    try { $app.Quit() } catch {}
}
