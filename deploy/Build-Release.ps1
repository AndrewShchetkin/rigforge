#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the React UI and publishes Windows and Linux release folders.

.DESCRIPTION
    1. npm ci && npm run build in ui/ (output: src/Host.Daemon/wwwroot)
    2. npm ci && npm run dist in shell/ (Windows Electron window)
    3. Publishes Host.Daemon and rigforge-scan for win-x64 and linux-x64
    4. Copies deploy/, effects/, and the Windows shell into the artifact folders

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

Push-Location (Join-Path $repo 'shell')
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed in shell/." }
    npm run dist
    if ($LASTEXITCODE -ne 0) { throw "Electron shell build failed." }
}
finally {
    Pop-Location
}

function Copy-RigForgeShell([string] $Repository, [string] $Destination) {
    $unpacked = Join-Path $Repository 'shell\dist\win-unpacked'
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'RigForge.exe'))) {
        throw "RigForge.exe was not produced at $unpacked."
    }

    Copy-Item -Path (Join-Path $unpacked '*') -Destination $Destination -Recurse -Force
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
    if ($Rid -eq 'win-x64') {
        Copy-RigForgeShell -Repository $repo -Destination $out
    }

    Write-Host "Published $out"
}

Publish-Rid 'win-x64'
Publish-Rid 'linux-x64'
