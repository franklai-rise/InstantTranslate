param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.4.1'
)

$ErrorActionPreference = 'Stop'

function Assert-LastExitCode {
    param([string]$Operation)

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionFile = Join-Path $projectRoot 'InstantTranslate.sln'
$projectFile = Join-Path $projectRoot 'src\InstantTranslate.App\InstantTranslate.App.csproj'
$artifactsRoot = Join-Path $projectRoot 'artifacts\release'
$packageName = "InstantTranslate-v$Version-win-x64"
$finalPackageDirectory = Join-Path $artifactsRoot $packageName
$finalZipPath = Join-Path $artifactsRoot "$packageName.zip"
$finalHashPath = "$finalZipPath.sha256"
$stagingRoot = Join-Path $artifactsRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
$stagingPackageDirectory = Join-Path $stagingRoot $packageName
$stagingZipPath = Join-Path $stagingRoot "$packageName.zip"
$assemblyVersion = "$Version.0"

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null

$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath($artifactsRoot)
$resolvedStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
$resolvedFinalPackage = [System.IO.Path]::GetFullPath($finalPackageDirectory)
if (-not $resolvedStagingRoot.StartsWith($resolvedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase) `
    -or -not $resolvedFinalPackage.StartsWith($resolvedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release paths resolved outside the release artifacts folder.'
}

try {
    & dotnet restore $solutionFile
    Assert-LastExitCode 'dotnet restore'

    & dotnet restore $projectFile --runtime win-x64
    Assert-LastExitCode 'dotnet restore (win-x64)'

    & dotnet build $solutionFile `
        --configuration Release `
        --no-restore `
        "/p:Version=$Version" `
        "/p:AssemblyVersion=$assemblyVersion" `
        "/p:FileVersion=$assemblyVersion" `
        "/p:InformationalVersion=$Version"
    Assert-LastExitCode 'dotnet build'

    & dotnet test $solutionFile `
        --configuration Release `
        --no-build `
        --no-restore
    Assert-LastExitCode 'dotnet test'

    & dotnet publish $projectFile `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $stagingPackageDirectory `
        "/p:Version=$Version" `
        "/p:AssemblyVersion=$assemblyVersion" `
        "/p:FileVersion=$assemblyVersion" `
        "/p:InformationalVersion=$Version" `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:EnableCompressionInSingleFile=true `
        /p:DebugType=None `
        /p:DebugSymbols=false
    Assert-LastExitCode 'dotnet publish'

    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $stagingPackageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'QUICK_START.txt') -Destination $stagingPackageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $stagingPackageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingPackageDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\InstantTranslate.App\Assets\AppLogo.png') -Destination $stagingPackageDirectory

    $packagedReadme = Join-Path $stagingPackageDirectory 'README.md'
    $readmeText = Get-Content -Raw -Encoding UTF8 -LiteralPath $packagedReadme
    $readmeText = $readmeText.Replace('src/InstantTranslate.App/Assets/AppLogo.png', 'AppLogo.png')
    Set-Content -Encoding UTF8 -LiteralPath $packagedReadme -Value $readmeText

    $publishedExecutable = Join-Path $stagingPackageDirectory 'InstantTranslate.exe'
    if (-not (Test-Path -LiteralPath $publishedExecutable)) {
        throw 'Published executable was not created.'
    }

    foreach ($smokeArgument in @('--smoke-test', '--popup-smoke-test')) {
        $smokeProcess = Start-Process `
            -FilePath $publishedExecutable `
            -ArgumentList $smokeArgument `
            -PassThru `
            -WindowStyle Hidden
        if (-not $smokeProcess.WaitForExit(15000)) {
            $smokeProcess.Kill($true)
            throw "Published executable $smokeArgument timed out."
        }
        if ($smokeProcess.ExitCode -ne 0) {
            throw "Published executable $smokeArgument failed with exit code $($smokeProcess.ExitCode)."
        }
    }

    Compress-Archive `
        -Path (Join-Path $stagingPackageDirectory '*') `
        -DestinationPath $stagingZipPath `
        -CompressionLevel Optimal

    if (-not (Test-Path -LiteralPath $stagingZipPath)) {
        throw 'Release archive was not created.'
    }

    if (Test-Path -LiteralPath $finalPackageDirectory) {
        Remove-Item -LiteralPath $finalPackageDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $finalZipPath) {
        Remove-Item -LiteralPath $finalZipPath -Force
    }
    if (Test-Path -LiteralPath $finalHashPath) {
        Remove-Item -LiteralPath $finalHashPath -Force
    }

    Move-Item -LiteralPath $stagingPackageDirectory -Destination $finalPackageDirectory
    Move-Item -LiteralPath $stagingZipPath -Destination $finalZipPath

    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $finalZipPath
    "$($hash.Hash.ToLowerInvariant()) *$([System.IO.Path]::GetFileName($finalZipPath))" |
        Set-Content -Encoding ASCII -LiteralPath $finalHashPath

    Write-Output "Package: $finalPackageDirectory"
    Write-Output "Archive: $finalZipPath"
    Write-Output "SHA-256: $finalHashPath"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
