param(
    [string]$Remote = "https://github.com/Sudfry/Locifab.git",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"

Write-Host "Locifab GitHub push (token is read securely and never written to files)" -ForegroundColor Cyan
Write-Host "Repository: $Remote"
Write-Host "Branch:     $Branch"
Write-Host ""

$secure = Read-Host "Paste GitHub Personal Access Token" -AsSecureString
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
    $authUrl = "https://x-access-token:$token@github.com/Sudfry/Locifab.git"
    git push $authUrl "$Branch`:$Branch"
    if ($LASTEXITCODE -ne 0) { throw "git push 失败，退出码 $LASTEXITCODE" }

    git remote remove origin 2>$null | Out-Null
    git remote add origin $Remote
    git branch --set-upstream-to="origin/$Branch" $Branch 2>$null | Out-Null
    Write-Host "`n推送成功。" -ForegroundColor Green
}
finally {
    $token = $null
    $authUrl = $null
}
