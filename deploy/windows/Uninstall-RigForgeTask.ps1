#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops RigForge and removes its logon task.

.DESCRIPTION
    Companion to Install-RigForgeTask.ps1. Asks the daemon to restore fan and AIO
    firmware control, then removes the scheduled task.
    Profile data under %LOCALAPPDATA%\RigForge is kept unless -PurgeProfileData is set.
    Pass -RemoveFiles to also delete the install directory and %LOCALAPPDATA%\RigForge\app.

.EXAMPLE
    .\deploy\windows\Uninstall-RigForgeTask.ps1

.EXAMPLE
    .\deploy\windows\Uninstall-RigForgeTask.ps1 -RemoveFiles -PurgeProfileData
#>
param(
    [string] $TaskName = 'RigForge',
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'RigForge'),
    [switch] $RemoveFiles,
    [switch] $PurgeProfileData
)

$ErrorActionPreference = 'Stop'

function Request-RigForgeShutdown {
    try {
        Invoke-WebRequest -UseBasicParsing -Method POST -Uri 'http://127.0.0.1:5757/api/host/shutdown' -TimeoutSec 3 | Out-Null
    }
    catch {
        Write-Host 'RigForge was not answering on port 5757; continuing shutdown.'
    }

    $deadline = (Get-Date).AddSeconds(4)
    while ((Get-Process -Name 'RigForge.Daemon' -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
        Start-Sleep -Milliseconds 250
    }
}

Request-RigForgeShutdown
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Get-Process -Name 'RigForge.Daemon', 'RigForge', 'rigforge-scan' -ErrorAction SilentlyContinue | Stop-Process -Force

$executable = Join-Path $InstallDirectory 'RigForge.Daemon.exe'
if (Test-Path -LiteralPath $executable) {
    & $executable --uninstall-task
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'RigForge.Daemon.exe --uninstall-task did not succeed; deleting the task directly.'
    }
}

# schtasks writes to stderr when the task is already gone. Keep that from aborting the script.
& schtasks.exe /Delete /TN $TaskName /F 1>$null 2>$null
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $task) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Unregistered scheduled task '$TaskName'."
}
else {
    Write-Host "Scheduled task '$TaskName' is not registered."
}

if ($RemoveFiles -and (Test-Path -LiteralPath $InstallDirectory)) {
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    Write-Host "Removed $InstallDirectory"
}

$profileRoot = Join-Path $env:LOCALAPPDATA 'RigForge'
if ($PurgeProfileData) {
    if (Test-Path -LiteralPath $profileRoot) {
        Remove-Item -LiteralPath $profileRoot -Recurse -Force
        Write-Host "Removed $profileRoot"
    }
    Write-Host 'RigForge task removal finished. Profile data was deleted.'
}
else {
    if ($RemoveFiles) {
        $legacyApp = Join-Path $profileRoot 'app'
        if (Test-Path -LiteralPath $legacyApp) {
            Remove-Item -LiteralPath $legacyApp -Recurse -Force
            Write-Host "Removed $legacyApp"
        }
    }

    Write-Host "RigForge task removal finished. Profile data in $profileRoot was kept."
}
