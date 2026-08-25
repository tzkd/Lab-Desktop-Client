[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version = '0.0.0-dev'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'LabDesktopClient.sln'
Push-Location $projectRoot
try {
    & (Join-Path $PSScriptRoot 'build-web.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Isaac viewer web build failed.' }

    & dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    & dotnet build $solution `
        --configuration $Configuration `
        --no-restore `
        -p:Version=$Version `
        -p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

    & dotnet test $solution `
        --configuration $Configuration `
        --no-build `
        --no-restore `
        -p:Version=$Version `
        -p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
}
finally {
    Pop-Location
}
