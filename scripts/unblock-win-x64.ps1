param(
    [string]$Path = (Join-Path $PSScriptRoot '..\publish\win-x64\LocalCraft.exe')
)

$exe = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
Unblock-File -LiteralPath $exe

$zone = Get-Item -LiteralPath $exe -Stream Zone.Identifier -ErrorAction SilentlyContinue
$signature = Get-AuthenticodeSignature -LiteralPath $exe

Write-Host "已清除下载锁定（Mark of the Web）：$exe"
if ($zone) {
    Write-Host "Zone.Identifier 仍存在。"
} else {
    Write-Host "Zone.Identifier 不存在。"
}
Write-Host "Authenticode 状态：$($signature.Status)"
if ($signature.Status -ne 'Valid') {
    Write-Host "说明：清除下载锁不等于代码签名。未签名 exe 仍可能触发 SmartScreen；要消除发布者提示，需要使用 CA 签发的代码签名证书。"
}
