param(
    [string]$Output = "publish\win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\LocalCraft.App\LocalCraft.App.csproj"
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) {
    $Output
} else {
    Join-Path $root $Output
}

Write-Host "Locifab - publishing self-contained single-file win-x64 build"
Write-Host "Project: $project"
Write-Host "Output:  $outputPath"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    -m:1 `
    --self-contained true `
    --ignore-failed-sources `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $outputPath "Locifab.exe"
if (-not (Test-Path $exe)) {
    throw "Publish finished but LocalCraft.exe was not found."
}

$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $exe"
Write-Host "Size: $sizeMb MB"
Write-Host "Copy this folder to a USB drive. No .NET or runtime installation is required."
