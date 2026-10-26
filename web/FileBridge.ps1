param(

    [int]$Port = 9501,

    [string]$Token = "LOCIFAB"

)



$ErrorActionPreference = "Stop"

$journalRoot = Join-Path $env:TEMP "LocifabWebBridge\journals"

New-Item -ItemType Directory -Force -Path $journalRoot | Out-Null

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)

$listener.Start()

Write-Host "Locifab FileBridge listening on 127.0.0.1:$Port"



function Send-Html($stream, [string]$htmlPath) {
    $body = [IO.File]::ReadAllBytes($htmlPath)
    $head = "HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nContent-Length: $($body.Length)`r`nCache-Control: no-store`r`nAccess-Control-Allow-Origin: *`r`nConnection: close`r`n`r`n"
    $hb = [Text.Encoding]::ASCII.GetBytes($head)
    $stream.Write($hb, 0, $hb.Length)
    $stream.Write($body, 0, $body.Length)
    $stream.Flush()
}

function Send-Response($stream, [int]$status, [string]$json) {

    $reason = if ($status -eq 200) { "OK" } elseif ($status -eq 204) { "No Content" } elseif ($status -eq 403) { "Forbidden" } elseif ($status -eq 404) { "Not Found" } else { "Error" }

    $body = [Text.Encoding]::UTF8.GetBytes($json)

    $head = "HTTP/1.1 $status $reason`r`nContent-Type: application/json; charset=utf-8`r`nContent-Length: $($body.Length)`r`nAccess-Control-Allow-Origin: *`r`nAccess-Control-Allow-Headers: Content-Type`r`nAccess-Control-Allow-Methods: POST, OPTIONS`r`nAccess-Control-Allow-Private-Network: true`r`nConnection: close`r`n`r`n"

    $hb = [Text.Encoding]::ASCII.GetBytes($head)

    $stream.Write($hb, 0, $hb.Length)

    if ($status -ne 204) { $stream.Write($body, 0, $body.Length) }

    $stream.Flush()

}



function Read-Request($stream) {

    $ms = [IO.MemoryStream]::new()

    $buf = New-Object byte[] 8192

    $headerEnd = -1

    while ($headerEnd -lt 0) {

        $n = $stream.Read($buf, 0, $buf.Length)

        if ($n -le 0) { break }

        $ms.Write($buf, 0, $n)

        $arr = $ms.ToArray()

        for ($i = 0; $i -le $arr.Length - 4; $i++) {

            if ($arr[$i] -eq 13 -and $arr[$i+1] -eq 10 -and $arr[$i+2] -eq 13 -and $arr[$i+3] -eq 10) {

                $headerEnd = $i; break

            }

        }

    }

    if ($headerEnd -lt 0) { return $null }

    $all = $ms.ToArray()

    $headerText = [Text.Encoding]::ASCII.GetString($all, 0, $headerEnd)

    $lines = $headerText -split "`r`n"

    $requestLine = $lines[0] -split ' '

    $headers = @{}

    foreach ($line in $lines[1..($lines.Length-1)]) {

        if ($line -match '^([^:]+):\s*(.*)$') { $headers[$matches[1].ToLower()] = $matches[2] }

    }

    $contentLength = 0

    if ($headers.ContainsKey('content-length')) { [int]::TryParse($headers['content-length'], [ref]$contentLength) | Out-Null }

    $bodyStart = $headerEnd + 4

    $have = $all.Length - $bodyStart

    while ($have -lt $contentLength) {

        $n = $stream.Read($buf, 0, $buf.Length)

        if ($n -le 0) { break }

        $ms.Write($buf, 0, $n)

        $all = $ms.ToArray()

        $have = $all.Length - $bodyStart

    }

    $body = ""

    if ($contentLength -gt 0 -and $all.Length -ge $bodyStart + $contentLength) {

        $body = [Text.Encoding]::UTF8.GetString($all, $bodyStart, $contentLength)

    }

    return @{ Method = $requestLine[0]; Path = $requestLine[1]; Headers = $headers; Body = $body }

}



function Get-TargetFiles($folder, [bool]$recursive, $extensions) {

    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "文件夹不存在：$folder" }

    $files = @(Get-ChildItem -LiteralPath $folder -File -Recurse:$recursive -Force -ErrorAction SilentlyContinue)

    $exts = @()

    if ($extensions) { $exts = @($extensions -split '[,; ]+' | Where-Object { $_ } | ForEach-Object { if ($_.StartsWith('.')) { $_.ToLower() } else { '.' + $_.ToLower() } }) }

    if ($exts.Count -gt 0) { $files = @($files | Where-Object { $exts -contains $_.Extension.ToLower() }) }

    return $files

}



function Format-Attrs([int]$value) {

    $names = @()

    if ($value -band 1) { $names += "只读" }

    if ($value -band 2) { $names += "隐藏" }

    if ($value -band 32) { $names += "存档" }

    if ($names.Count -eq 0) { $names += "普通" }

    return ($names -join " / ")

}



function Build-Plan($data) {

    $folder = [string]$data.folder

    $recursive = [bool]$data.recursive

    $exts = [string]$data.extensions

    $mode = [string]$data.mode

    $fixed = [DateTime]::Now

    if ($data.fixedTime) { [DateTime]::TryParse([string]$data.fixedTime, [ref]$fixed) | Out-Null }

    $offset = 0.0

    if ($null -ne $data.offsetDays) { [double]::TryParse([string]$data.offsetDays, [ref]$offset) | Out-Null }

    $applyCreated = [bool]$data.applyCreated

    $applyModified = [bool]$data.applyModified

    $applyAccessed = [bool]$data.applyAccessed

    $setReadOnly = [bool]$data.setReadOnly

    $setHidden = [bool]$data.setHidden

    $setArchive = [bool]$data.setArchive

    $remove = [bool]$data.remove



    $rows = @()

    foreach ($f in (Get-TargetFiles $folder $recursive $exts)) {

        try {

            $p = $f.FullName

            $oldCreated = [IO.File]::GetCreationTime($p)

            $oldModified = [IO.File]::GetLastWriteTime($p)

            $oldAccessed = [IO.File]::GetLastAccessTime($p)

            $oldAttrs = [int][IO.File]::GetAttributes($p)

            if ($mode -eq 'attributes') {

                $flag = 0

                if ($setReadOnly) { $flag = $flag -bor 1 }

                if ($setHidden) { $flag = $flag -bor 2 }

                if ($setArchive) { $flag = $flag -bor 32 }

                $newAttrs = if ($remove) { $oldAttrs -band (-bnot $flag) } else { $oldAttrs -bor $flag }

                $rows += [pscustomobject]@{

                    path = $p; name = $f.Name

                    oldText = (Format-Attrs $oldAttrs); newText = (Format-Attrs $newAttrs)

                    oldModified = $oldModified.ToString('o'); newModified = $oldModified.ToString('o')

                    oldCreated = $oldCreated.ToString('o'); newCreated = $oldCreated.ToString('o')

                    oldAccessed = $oldAccessed.ToString('o'); newAccessed = $oldAccessed.ToString('o')

                    oldAttrs = $oldAttrs; newAttrs = $newAttrs

                }

            } else {

                $newCreated = if ($data.offsetMode) { $oldCreated.AddDays($offset) } else { $fixed }

                $newModified = if ($data.offsetMode) { $oldModified.AddDays($offset) } else { $fixed }

                $newAccessed = if ($data.offsetMode) { $oldAccessed.AddDays($offset) } else { $fixed }

                $rows += [pscustomobject]@{

                    path = $p; name = $f.Name

                    oldText = $oldModified.ToString('yyyy-MM-dd HH:mm:ss'); newText = $newModified.ToString('yyyy-MM-dd HH:mm:ss')

                    oldModified = $oldModified.ToString('o'); newModified = $newModified.ToString('o')

                    oldCreated = $oldCreated.ToString('o'); newCreated = $newCreated.ToString('o')

                    oldAccessed = $oldAccessed.ToString('o'); newAccessed = $newAccessed.ToString('o')

                    oldAttrs = $oldAttrs; newAttrs = $oldAttrs

                }

            }

        } catch { }

    }

    return $rows

}



while ($true) {

    $client = $listener.AcceptTcpClient()

    try {

        $stream = $client.GetStream()

        $req = Read-Request $stream

        if ($null -eq $req) { continue }

        if ($req.Method -eq 'OPTIONS') { Send-Response $stream 204 "{}"; continue }

        $uri = [Uri]("http://127.0.0.1" + $req.Path)

        $path = $uri.AbsolutePath

        if ($path -eq '/' -or $path -eq '/Locifab-Extended.html') {
            $htmlPath = Join-Path $PSScriptRoot 'Locifab-Extended.html'
            if (Test-Path -LiteralPath $htmlPath) { Send-Html $stream $htmlPath; continue }
        }

        $query = @{}

        foreach ($p in ($uri.Query.TrimStart('?') -split '&')) {

            $kv = $p -split '=', 2

            if ($kv.Count -eq 2) { $query[$kv[0]] = [Uri]::UnescapeDataString($kv[1]) }

        }

        if ($query['token'] -ne $Token) { Send-Response $stream 403 '{"ok":false,"error":"forbidden"}'; continue }

        $data = if ($req.Body) { $req.Body | ConvertFrom-Json } else { [pscustomobject]@{} }

        if ($path -eq '/api/file-state/preview') {

            $rows = Build-Plan $data

            $result = [pscustomobject]@{ rows = @($rows); count = @($rows).Count }

            Send-Response $stream 200 (([pscustomobject]@{ ok = $true; result = $result } | ConvertTo-Json -Depth 8 -Compress))

        }

        elseif ($path -eq '/api/file-state/apply') {

            $rows = Build-Plan $data

            if (@($rows).Count -eq 0) { Send-Response $stream 200 '{"ok":false,"error":"没有匹配的文件"}'; continue }

            $id = [guid]::NewGuid().ToString('N')

            $journal = [pscustomobject]@{ id = $id; mode = [string]$data.mode; createdAt = [DateTime]::Now.ToString('o'); entries = @($rows) }

            $journalPath = Join-Path $journalRoot ($id + '.json')

            ($journal | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $journalPath -Encoding UTF8

            $done = 0

            foreach ($r in $rows) {

                try {

                    if ([string]$data.mode -eq 'attributes') {

                        [IO.File]::SetAttributes($r.path, [IO.FileAttributes]([int]$r.newAttrs))

                    } else {

                        if ([bool]$data.applyCreated) { [IO.File]::SetCreationTime($r.path, [DateTime]::Parse([string]$r.newCreated)) }

                        if ([bool]$data.applyModified) { [IO.File]::SetLastWriteTime($r.path, [DateTime]::Parse([string]$r.newModified)) }

                        if ([bool]$data.applyAccessed) { [IO.File]::SetLastAccessTime($r.path, [DateTime]::Parse([string]$r.newAccessed)) }

                    }

                    $done++

                } catch { }

            }

            Send-Response $stream 200 (([pscustomobject]@{ ok = $true; result = [pscustomobject]@{ id = $id; count = $done; total = @($rows).Count } } | ConvertTo-Json -Depth 6 -Compress))

        }

        elseif ($path -eq '/api/file-state/undo') {

            $id = [string]$data.id

            if ($id -notmatch '^[a-f0-9]{32}$') { Send-Response $stream 200 '{"ok":false,"error":"无效的日志编号"}'; continue }

            $journalPath = Join-Path $journalRoot ($id + '.json')

            if (-not (Test-Path -LiteralPath $journalPath)) { Send-Response $stream 200 '{"ok":false,"error":"日志不存在"}'; continue }

            $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json

            $done = 0

            foreach ($e in $journal.entries) {

                try {

                    $current = [int][IO.File]::GetAttributes($e.path)

                    if ($current -band 1) { [IO.File]::SetAttributes($e.path, [IO.FileAttributes]($current -band (-bnot 1))) }

                    [IO.File]::SetCreationTime($e.path, [DateTime]::Parse([string]$e.oldCreated))

                    [IO.File]::SetLastWriteTime($e.path, [DateTime]::Parse([string]$e.oldModified))

                    [IO.File]::SetLastAccessTime($e.path, [DateTime]::Parse([string]$e.oldAccessed))

                    [IO.File]::SetAttributes($e.path, [IO.FileAttributes]([int]$e.oldAttrs))

                    $done++

                } catch { }

            }

            Send-Response $stream 200 (([pscustomobject]@{ ok = $true; result = [pscustomobject]@{ count = $done } } | ConvertTo-Json -Depth 6 -Compress))

        }

        else {

            Send-Response $stream 404 '{"ok":false,"error":"not found"}'

        }

    } catch {

        try { Send-Response $stream 500 (([pscustomobject]@{ ok = $false; error = $_.Exception.Message } | ConvertTo-Json -Compress)) } catch { }

    } finally {

        $client.Close()

    }

}

