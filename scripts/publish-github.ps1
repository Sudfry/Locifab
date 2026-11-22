param(
    [string]$Remote = "https://github.com/Sudfry/Locifab.git",
    [string]$Branch = "main",
    [string]$Message = "Update Locifab source to 11.1.0",
    [switch]$NoCommit
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (!(Test-Path (Join-Path $root ".git"))) {
    throw "当前目录不是 Git 仓库：$root"
}

Write-Host "Locifab GitHub publish" -ForegroundColor Cyan
Write-Host "Repository: $Remote"
Write-Host "Branch:     $Branch"
Write-Host ""

git config user.name "Sudfry" 2>$null | Out-Null
git config user.email "Sudfry@users.noreply.github.com" 2>$null | Out-Null

git add -A
if (!$NoCommit) {
    $changes = git status --porcelain
    if ([string]::IsNullOrWhiteSpace($changes)) {
        Write-Host "没有需要提交的改动。" -ForegroundColor Yellow
    }
    else {
        git commit -m $Message
        if ($LASTEXITCODE -ne 0) { throw "git commit 失败" }
    }
}

$secure = Read-Host "Paste GitHub Personal Access Token (repo / Contents write)" -AsSecureString
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
    $authUrl = "https://x-access-token:${token}@github.com/Sudfry/Locifab.git"
    git push $authUrl "${Branch}:${Branch}"
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "推送失败。请检查网络或 token 权限。" -ForegroundColor Yellow
        Write-Host "不会修改本地提交，可稍后重试。" -ForegroundColor Yellow
        exit $LASTEXITCODE
    }

    git remote remove origin 2>$null | Out-Null
    git remote add origin $Remote
    git branch --set-upstream-to="origin/$Branch" $Branch 2>$null | Out-Null

    Write-Host ""
    Write-Host "推送成功。" -ForegroundColor Green
    git log --oneline -1
    Write-Host ""
    Write-Host "仓库：https://github.com/Sudfry/Locifab" -ForegroundColor Cyan
}
finally {
    $token = $null
    $authUrl = $null
}
