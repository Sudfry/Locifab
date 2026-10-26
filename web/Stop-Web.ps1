$pids = @()
foreach ($port in @(9500, 9501)) {
    $matches = netstat -ano | Select-String (":$port\s+.*LISTENING\s+(\d+)")
    foreach ($m in $matches) {
        if ($m.Matches.Count -gt 0) { $pids += [int]$m.Matches[0].Groups[1].Value }
    }
}
$pids = $pids | Sort-Object -Unique
foreach ($processId in $pids) {
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}
Write-Host ("Stopped PIDs: " + ($pids -join ", "))
