param(
    [string]$ExePath = "",
    [string]$RepoUrl = "https://github.com/Sudfry/Locifab.git",
    [string]$Branch = "main",
    [string]$Destination = "downloads/Locifab.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $root "Locifab-11.0.3\Locifab.exe"
}
if (!(Test-Path $ExePath)) {
    throw "找不到 EXE：$ExePath"
}

$exe = Get-Item $ExePath
if ($exe.Length -gt 100MB) {
    throw "文件超过 GitHub 单文件 100 MB 限制：$([math]::Round($exe.Length / 1MB, 1)) MB"
}

Write-Host "Locifab raw download upload" -ForegroundColor Cyan
Write-Host "Source: $($exe.FullName)"
Write-Host "Target: $Destination"
Write-Host "Size:   $([math]::Round($exe.Length / 1MB, 1)) MB"
Write-Host ""

$secure = Read-Host "Paste GitHub Personal Access Token (repo contents write)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Token 为空，已取消。"
}

try {
    Set-Location $root

    $targetDir = Split-Path -Parent (Join-Path $root $Destination)
    New-Item -ItemType Directory $targetDir -Force | Out-Null
    Copy-Item $exe.FullName (Join-Path $root $Destination) -Force

    git config http.postBuffer 524288000 | Out-Null
    git add --force $Destination

    $changes = git status --porcelain -- $Destination
    if ([string]::IsNullOrWhiteSpace($changes)) {
        Write-Host "文件内容未变化，仍执行推送检查。" -ForegroundColor Yellow
    }
    else {
        git commit -m "Publish portable Locifab.exe for Microsoft Store direct URL" | Out-Null
    }

    $authUrl = "https://x-access-token:${token}@github.com/Sudfry/Locifab.git"
    git push $authUrl "${Branch}:${Branch}"
    if ($LASTEXITCODE -ne 0) { throw "git push 失败，退出码 $LASTEXITCODE" }

    git remote remove origin 2>$null | Out-Null
    git remote add origin $RepoUrl
    git branch --set-upstream-to="origin/$Branch" $Branch 2>$null | Out-Null

    $raw = "https://raw.githubusercontent.com/Sudfry/Locifab/$Branch/$Destination"
    Write-Host ""
    Write-Host "上传完成。" -ForegroundColor Green
    Write-Host "包 URL 填：" -ForegroundColor Green
    Write-Host $raw -ForegroundColor Cyan
    Write-Host ""
    Write-Host "验证命令：" -ForegroundColor Yellow
    Write-Host "curl.exe -I `"$raw`""
}
finally {
    $token = $null
    $authUrl = $null
}
