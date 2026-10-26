$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:NUGET_PACKAGES = Join-Path $root "nuget-cache"
$env:DOTNET_CLI_HOME = Join-Path $root ".dotnet-cli"

Write-Host "Locifab full test" -ForegroundColor Cyan

Write-Host "`n[1/2] 核心自测" -ForegroundColor Yellow
dotnet run --project (Join-Path $root "tests\LocalCraft.SelfTest\LocalCraft.SelfTest.csproj") `
    -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw "核心自测失败，退出码 $LASTEXITCODE" }

Write-Host "`n[2/2] 流水线端到端测试" -ForegroundColor Yellow
dotnet build (Join-Path $root "tests\Harness\Harness.csproj") -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "测试工具构建失败，退出码 $LASTEXITCODE" }

dotnet (Join-Path $root "tests\Harness\bin\Release\net8.0-windows\Harness.dll")
if ($LASTEXITCODE -ne 0) { throw "流水线测试失败，退出码 $LASTEXITCODE" }

Write-Host "`n全部测试通过。" -ForegroundColor Green
