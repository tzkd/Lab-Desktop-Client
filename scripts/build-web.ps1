[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'web\isaac'

foreach ($command in @('node', 'npm')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "$command was not found. Install Node.js 20 or newer."
    }
}

function Assert-MinimumVersion {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [version] $Minimum
    )

    $raw = (& $Command --version).Trim().TrimStart('v')
    if ($LASTEXITCODE -ne 0) { throw "$Command --version failed." }
    $parsed = $null
    if (-not [version]::TryParse(($raw -split '-', 2)[0], [ref] $parsed) -or $parsed -lt $Minimum) {
        throw "$Command $Minimum or newer is required; found $raw."
    }
}

Assert-MinimumVersion -Command 'node' -Minimum ([version] '20.0.0')
Assert-MinimumVersion -Command 'npm' -Minimum ([version] '10.0.0')

Push-Location $webRoot
try {
    & npm ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }

    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Isaac viewer web build failed.' }
}
finally {
    Pop-Location
}
