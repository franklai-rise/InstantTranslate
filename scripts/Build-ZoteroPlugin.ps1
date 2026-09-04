[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceDirectory = Join-Path $projectRoot 'integrations\zotero\instant-translate-selection'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'artifacts\integrations'
}

$resolvedSource = (Resolve-Path -LiteralPath $sourceDirectory).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

foreach ($requiredFile in @('manifest.json', 'bootstrap.js', 'README.md')) {
    $requiredPath = Join-Path $resolvedSource $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing Zotero plugin file: $requiredFile"
    }
}

$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $resolvedSource 'manifest.json') |
    ConvertFrom-Json
if ($manifest.applications.zotero.id -ne 'instant-translate-selection@franklai.local') {
    throw 'Unexpected Zotero plugin id.'
}

$temporaryZip = Join-Path $resolvedOutput ('.zotero-' + [Guid]::NewGuid().ToString('N') + '.zip')
$xpiPath = Join-Path $resolvedOutput 'InstantTranslate-Zotero-Selection.xpi'
try {
    Compress-Archive -Path (Join-Path $resolvedSource '*') -DestinationPath $temporaryZip -CompressionLevel Optimal
    Move-Item -LiteralPath $temporaryZip -Destination $xpiPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryZip) {
        Remove-Item -LiteralPath $temporaryZip -Force
    }
}

Write-Output $xpiPath
