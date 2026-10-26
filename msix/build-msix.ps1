param(
    [string]$PackageName = "Sudfry.Locifab",
    [string]$Publisher = "CN=Sudfry",
    [string]$Version = "11.0.3.0",
    [string]$CertPath = "",
    [string]$CertPassword = "",
    [string]$Output = "Locifab-11.0.3.msix"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$layout = Join-Path $PSScriptRoot "layout"
$manifest = Join-Path $layout "AppxManifest.xml"
$publish = Join-Path $root "publish\msix-x64"
$makeappx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

if (!(Test-Path $publish)) {
    throw "缺少 $publish，请先执行：dotnet publish src\LocalCraft.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish\msix-x64"
}
if (!(Test-Path $manifest)) {
    throw "缺少 $manifest"
}

# 同步应用文件，保留 AppxManifest.xml 与 Assets
Get-ChildItem $layout -Force | Where-Object { $_.Name -notin @("AppxManifest.xml", "Assets") } | Remove-Item -Recurse -Force
Copy-Item (Join-Path $publish "*") $layout -Recurse -Force

# 写入 Partner Center 提供的 Identity
[xml]$xml = Get-Content $manifest -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace("f", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identity = $xml.SelectSingleNode("//f:Identity", $ns)
$identity.SetAttribute("Name", $PackageName)
$identity.SetAttribute("Publisher", $Publisher)
$identity.SetAttribute("Version", $Version)
$xml.Save($manifest)

$outPath = Join-Path $PSScriptRoot $Output
& $makeappx pack /o /d $layout /p $outPath
if ($LASTEXITCODE -ne 0) { throw "MakeAppx 打包失败" }

if ($CertPath) {
    & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword $outPath
    if ($LASTEXITCODE -ne 0) { throw "签名失败" }
    & $signtool verify /pa /v $outPath
    if ($LASTEXITCODE -ne 0) { throw "签名验证失败" }
}
else {
    Write-Host "未提供证书，已生成未签名包。Store 提交前请签名。" -ForegroundColor Yellow
}

Write-Host "完成：$outPath" -ForegroundColor Green
