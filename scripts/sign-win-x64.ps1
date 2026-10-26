param(
    [string]$ExePath = (Join-Path $PSScriptRoot "..\publish\win-x64\Locifab.exe"),
    [string]$PfxPath = "",
    [string]$Password = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$exe = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $exe)) {
    throw "找不到可执行文件：$exe"
}

if ([string]::IsNullOrWhiteSpace($PfxPath) -or -not (Test-Path -LiteralPath $PfxPath)) {
    Write-Host "未提供代码签名证书（.pfx）。" -ForegroundColor Yellow
    Write-Host "仅设置 Company=Sudfry 不会改变 Windows 的发布者验证；要显示 Sudfry，需要 CA 签发的代码签名证书。"
    Write-Host "用法：powershell -ExecutionPolicy Bypass -File scripts\sign-win-x64.ps1 -PfxPath C:\path\sudfry.pfx -Password <密码>"
    exit 1
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
if (-not $signtool) {
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $signtool) {
    throw "未找到 signtool.exe。请安装 Windows SDK Signing Tools。"
}

& $signtool sign /fd SHA256 /f $PfxPath /p $Password /tr $TimestampUrl /td SHA256 $exe
if ($LASTEXITCODE -ne 0) {
    throw "代码签名失败，退出码 $LASTEXITCODE。"
}

Get-AuthenticodeSignature -FilePath $exe | Format-List Status, StatusMessage, SignerCertificate
