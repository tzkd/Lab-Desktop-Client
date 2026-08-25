[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string] $Runtime = 'win-x64',

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version = '0.0.0-dev',

    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\LabDesktop.Client.App\LabDesktop.Client.App.csproj'
$output = Join-Path $projectRoot "artifacts\publish\$Runtime"

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -Version $Version
    if ($LASTEXITCODE -ne 0) { throw 'Build validation failed.' }
}
else {
    & (Join-Path $PSScriptRoot 'build-web.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Isaac viewer web build failed.' }
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

& dotnet restore $project --runtime $Runtime
if ($LASTEXITCODE -ne 0) { throw 'Runtime-specific restore failed.' }

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    --no-restore `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$requiredFiles = @(
    'LabDesktopClient.exe',
    'WebView2Loader.dll',
    'Web\Isaac\index.html'
)
foreach ($relativePath in $requiredFiles) {
    $publishedPath = Join-Path $output $relativePath
    if (-not (Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
        throw "Published client is incomplete: missing $relativePath"
    }
}

Write-Host "Published: $output"
