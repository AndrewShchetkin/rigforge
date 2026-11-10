#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Stops RigForge and removes its logon task.

.DESCRIPTION
    Companion to Install-RigForgeTask.ps1. Stops the daemon process, unregisters the
    scheduled task, and optionally deletes the published app folder.
    Profile data under %LOCALAPPDATA%\RigForge (profile.json) is kept.
    Pass -RemoveFiles to also delete the install directory and %LOCALAPPDATA%\RigForge\app.

.EXAMPLE
    .\deploy\windows\Uninstall-RigForgeTask.ps1

.EXAMPLE
    .\deploy\windows\Uninstall-RigForgeTask.ps1 -RemoveFiles
#>
param(
    [string] $TaskName = 'RigForge',
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'RigForge'),
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'Stop'

Get-Process -Name 'RigForge.Daemon' -ErrorAction SilentlyContinue | Stop-Process -Force

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $task) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Unregistered scheduled task '$TaskName'."
}
else {
    Write-Host "Scheduled task '$TaskName' was not registered."
}

if ($RemoveFiles) {
    $appData = Join-Path $env:LOCALAPPDATA 'RigForge\app'
    $targets = @($InstallDirectory, $appData) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    foreach ($dir in $targets) {
        if (Test-Path -LiteralPath $dir) {
            Remove-Item -LiteralPath $dir -Recurse -Force
            Write-Host "Removed $dir"
        }
    }
}

Write-Host "RigForge task removal finished. Profile data in $env:LOCALAPPDATA\RigForge was kept."
