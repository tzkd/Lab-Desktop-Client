[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string] $Version,

    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$runtime = 'win-x64'
$publishDirectory = Join-Path $projectRoot "artifacts\publish\$runtime"
$installerDirectory = Join-Path $projectRoot 'artifacts\installer'
$releaseDirectory = Join-Path $projectRoot 'artifacts\release'
$portableName = "LabDesktopClient-$Version-$runtime-portable.zip"
$installerName = "LabDesktopClient-$Version-$runtime-setup.exe"
$checksumsName = "LabDesktopClient-$Version-SHA256SUMS.txt"

$compilerCandidates = [System.Collections.Generic.List[string]]::new()
$compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
if ($compilerCommand) {
    $compilerCandidates.Add($compilerCommand.Source)
}

foreach ($root in @(${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
    if ($root) {
        $compilerCandidates.Add((Join-Path $root 'Programs\Inno Setup 6\ISCC.exe'))
        $compilerCandidates.Add((Join-Path $root 'Inno Setup 6\ISCC.exe'))
    }
}

$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install it before creating release artifacts.'
}

& (Join-Path $PSScriptRoot 'publish.ps1') `
    -Runtime $runtime `
    -Version $Version `
    -SkipTests:$SkipTests
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

$application = Join-Path $publishDirectory 'LabDesktopClient.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Published application is missing: $application"
}

$productVersion = (Get-Item -LiteralPath $application).VersionInfo.ProductVersion
if ($productVersion -ne $Version) {
    throw "Published application version '$productVersion' does not match '$Version'."
}

foreach ($directory in @($installerDirectory, $releaseDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory | Out-Null
}

$installerScript = Join-Path $projectRoot 'installer\LabDesktopClient.iss'
& $compiler "/DAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup packaging failed.' }

$installer = Join-Path $installerDirectory $installerName
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer is missing: $installer"
}

$installerVersionInfo = (Get-Item -LiteralPath $installer).VersionInfo
$installerProductVersion = ([string] $installerVersionInfo.ProductVersion).Trim()
$installerFileVersion = ([string] $installerVersionInfo.FileVersion).Trim()
if ($installerProductVersion -ne $Version -or $installerFileVersion -ne "$Version.0") {
    throw "Installer version does not match '$Version': product='$installerProductVersion', file='$installerFileVersion'."
}

$stagingDirectory = Join-Path $releaseDirectory 'portable'
New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
try {
    Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $stagingDirectory -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\THIRD-PARTY-NOTICES.md') -Destination $stagingDirectory

    $portable = Join-Path $releaseDirectory $portableName
    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $portable -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

$releaseInstaller = Join-Path $releaseDirectory $installerName
Copy-Item -LiteralPath $installer -Destination $releaseInstaller

$releaseAssets = @(
    Get-Item -LiteralPath $releaseInstaller
    Get-Item -LiteralPath (Join-Path $releaseDirectory $portableName)
)
$checksumLines = foreach ($asset in $releaseAssets | Sort-Object Name) {
    $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($asset.Name)"
}
$checksums = Join-Path $releaseDirectory $checksumsName
Set-Content -LiteralPath $checksums -Value $checksumLines -Encoding ascii

Write-Host 'Release artifacts complete'
Write-Host "Version: $Version"
Write-Host "Directory: $releaseDirectory"
$releaseAssets.Name + $checksumsName | ForEach-Object { Write-Host "  $_" }
