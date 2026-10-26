$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "tests\LocalCraft.SelfTest\LocalCraft.SelfTest.csproj"

Push-Location $root
try {
    dotnet run --project $project -c Release --nologo
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
