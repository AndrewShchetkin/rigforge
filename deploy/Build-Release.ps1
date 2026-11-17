#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Windows NSIS setup, a portable zip, and the Linux archive.

.DESCRIPTION
    Reads the version from Directory.Build.props, builds the React UI unless -SkipUi,
    publishes the daemon and scanner into one shared win-x64 folder, then runs electron-builder.
    Linux publish is skipped with -SkipLinux. -FrameworkDependent publishes without a bundled runtime.

.EXAMPLE
    .\deploy\Build-Release.ps1 -SkipLinux
#>
param(
    [switch] $SkipUi,
    [switch] $SkipLinux,
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $repo 'artifacts'
$stage = Join-Path $artifacts 'stage'
$stageDaemon = Join-Path $stage 'win-x64-daemon'

function Get-RigForgeVersion([string] $Repository) {
    $propsPath = Join-Path $Repository 'Directory.Build.props'
    $text = Get-Content -LiteralPath $propsPath -Raw
    $match = [regex]::Match($text, '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw "Version element was not found in $propsPath."
    }

    return $match.Groups[1].Value.Trim()
}

function Invoke-Native([scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

function Publish-RigForgeProject([string] $Project, [string] $Runtime, [string] $Output, [string] $SelfContained, [string] $Version) {
    & dotnet publish $Project `
        -c Release `
        -r $Runtime `
        --self-contained $SelfContained `
        -o $Output `
        -p:PublishSingleFile=false `
        "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project ($Runtime)."
    }
}

$version = Get-RigForgeVersion $repo
$selfContained = 'true'
if ($FrameworkDependent) {
    $selfContained = 'false'
}

$daemonProject = Join-Path $repo 'src\Host.Daemon\RigForge.Host.Daemon.csproj'
$scannerProject = Join-Path $repo 'src\Tools.HardwareScanner\RigForge.Tools.HardwareScanner.csproj'
$releaseFiles = @()

try {
    if (-not $SkipUi) {
        Push-Location (Join-Path $repo 'ui')
        try {
            Invoke-Native { npm ci }
            Invoke-Native { npm run build }
        }
        finally {
            Pop-Location
        }
    }

    if (Test-Path -LiteralPath $stageDaemon) {
        Remove-Item -LiteralPath $stageDaemon -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageDaemon -Force | Out-Null

    # Scanner first, daemon second, so both executables share one .NET / ASP.NET Core runtime.
    Publish-RigForgeProject $scannerProject 'win-x64' $stageDaemon $selfContained $version
    Publish-RigForgeProject $daemonProject 'win-x64' $stageDaemon $selfContained $version

    Push-Location (Join-Path $repo 'shell')
    try {
        Invoke-Native { npm ci }
        Invoke-Native { npx electron-builder --win nsis dir "-c.extraMetadata.version=$version" }
    }
    finally {
        Pop-Location
    }

    $setupSource = Join-Path $repo "shell\dist\RigForge-Setup-$version-win-x64.exe"
    if (-not (Test-Path -LiteralPath $setupSource)) {
        throw "Installer was not produced at $setupSource."
    }

    New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
    $setupDest = Join-Path $artifacts "RigForge-Setup-$version-win-x64.exe"
    Copy-Item -LiteralPath $setupSource -Destination $setupDest -Force
    $releaseFiles += $setupDest

    $unpacked = Join-Path $repo 'shell\dist\win-unpacked'
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'RigForge.exe'))) {
        throw "RigForge.exe was not produced at $unpacked."
    }

    $portableDir = Join-Path $artifacts 'rigforge-win-x64'
    if (Test-Path -LiteralPath $portableDir) {
        Remove-Item -LiteralPath $portableDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
    Copy-Item -Path (Join-Path $unpacked '*') -Destination $portableDir -Recurse -Force

    $portableZip = Join-Path $artifacts "rigforge-win-x64-$version.zip"
    if (Test-Path -LiteralPath $portableZip) {
        Remove-Item -LiteralPath $portableZip -Force
    }
    & tar.exe -a -cf $portableZip -C $portableDir .
    if ($LASTEXITCODE -ne 0) { throw "Failed to create $portableZip." }
    $releaseFiles += $portableZip

    if (-not $SkipLinux) {
        $linuxOut = Join-Path $artifacts 'rigforge-linux-x64'
        if (Test-Path -LiteralPath $linuxOut) {
            Remove-Item -LiteralPath $linuxOut -Recurse -Force
        }
        New-Item -ItemType Directory -Path $linuxOut -Force | Out-Null
        Publish-RigForgeProject $scannerProject 'linux-x64' $linuxOut $selfContained $version
        Publish-RigForgeProject $daemonProject 'linux-x64' $linuxOut $selfContained $version

        $linuxDeploy = Join-Path $linuxOut 'deploy\linux'
        New-Item -ItemType Directory -Path $linuxDeploy -Force | Out-Null
        Copy-Item -Path (Join-Path $repo 'deploy\linux\*') -Destination $linuxDeploy -Force
        Copy-Item -Path (Join-Path $repo 'effects') -Destination (Join-Path $linuxOut 'effects') -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $repo 'ui\public\favicon.svg') -Destination (Join-Path $linuxDeploy 'rigforge.svg') -Force

        $linuxArchive = Join-Path $artifacts "rigforge-linux-x64-$version.tar.gz"
        if (Test-Path -LiteralPath $linuxArchive) {
            Remove-Item -LiteralPath $linuxArchive -Force
        }
        & tar.exe -czf $linuxArchive -C $artifacts 'rigforge-linux-x64'
        if ($LASTEXITCODE -ne 0) { throw "Failed to create $linuxArchive." }
        $releaseFiles += $linuxArchive
    }

    $checksumLines = foreach ($file in $releaseFiles) {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash.ToLowerInvariant()
        '{0}  {1}' -f $hash, (Split-Path -Leaf $file)
    }
    Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value ($checksumLines -join "`n") -Encoding ascii
    Write-Host "Release $version is in $artifacts"
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
