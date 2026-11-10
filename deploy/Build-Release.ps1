#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the React UI and publishes Windows and Linux release folders.

.DESCRIPTION
    1. npm ci && npm run build in ui/ (output: src/Host.Daemon/wwwroot)
    2. Publishes Host.Daemon and rigforge-scan for win-x64 and linux-x64
    3. Copies deploy/ and effects/ into each artifact folder

.EXAMPLE
    .\deploy\Build-Release.ps1
#>
param(
    [switch] $SkipUi
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')

if (-not $SkipUi) {
    Push-Location (Join-Path $repo 'ui')
    try {
        npm ci
        npm run build
    }
    finally {
        Pop-Location
    }
}

function Publish-Rid([string] $Rid) {
    $out = Join-Path $repo "artifacts\rigforge-$Rid"
    if (Test-Path $out) {
        Remove-Item -LiteralPath $out -Recurse -Force
    }

    dotnet publish (Join-Path $repo 'src\Host.Daemon\RigForge.Host.Daemon.csproj') `
        -c Release -r $Rid --self-contained false -o $out
    dotnet publish (Join-Path $repo 'src\Tools.HardwareScanner\RigForge.Tools.HardwareScanner.csproj') `
        -c Release -r $Rid --self-contained false -o $out

    Copy-Item -Path (Join-Path $repo 'deploy') -Destination (Join-Path $out 'deploy') -Recurse -Force
    Copy-Item -Path (Join-Path $repo 'effects') -Destination (Join-Path $out 'effects') -Recurse -Force
    Write-Host "Published $out"
}

Publish-Rid 'win-x64'
Publish-Rid 'linux-x64'
