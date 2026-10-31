#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publishes RigForge and registers it to start at logon.

.DESCRIPTION
    The daemon runs as a logon task instead of a Windows service because DXGI Desktop Duplication
    (Ambilight) only works inside the interactive user session. It runs elevated so that
    LibreHardwareMonitor can use the PawnIO driver for CPU temperatures and the Super I/O chip.

.EXAMPLE
    .\deploy\windows\Install-RigForgeTask.ps1
#>
param(
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'RigForge'),
    [string] $TaskName = 'RigForge',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$repository = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if (-not $SkipPublish) {
    Push-Location (Join-Path $repository 'ui')
    try {
        npm ci
        npm run build
    }
    finally {
        Pop-Location
    }

    dotnet publish (Join-Path $repository 'src\Host.Daemon\RigForge.Host.Daemon.csproj') `
        -c Release -r win-x64 --self-contained false -o $InstallDirectory
}

$executable = Join-Path $InstallDirectory 'RigForge.Daemon.exe'
if (-not (Test-Path $executable)) {
    throw "RigForge.Daemon.exe not found in $InstallDirectory."
}

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $executable -WorkingDirectory $InstallDirectory
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
    -Description 'RigForge: RGB, fan/pump and AIO screen daemon' -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "RigForge is running. Open http://127.0.0.1:5757 for the configurator."
Write-Host "Close NZXT CAM, SignalRGB and Armoury Crate's LightingService: they fight over the same controllers."
