param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'src\InstantTranslate.App\InstantTranslate.App.csproj'
$artifactsRoot = Join-Path $projectRoot 'artifacts\release'
$packageName = "InstantTranslate-v$Version-win-x64"
$packageDirectory = Join-Path $artifactsRoot $packageName
$zipPath = Join-Path $artifactsRoot "$packageName.zip"

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null

$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
$resolvedPackageDirectory = [System.IO.Path]::GetFullPath($packageDirectory)
if (-not $resolvedPackageDirectory.StartsWith($resolvedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Package directory resolved outside the release artifacts folder.'
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $packageDirectory `
    /p:Version=$Version `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:DebugType=None `
    /p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output "Package: $packageDirectory"
Write-Output "Archive: $zipPath"
