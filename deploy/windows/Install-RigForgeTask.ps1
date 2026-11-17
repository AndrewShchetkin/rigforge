#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Local developer helper. Not the supported install path and not shipped in the release.

.DESCRIPTION
    The supported Windows install is artifacts\RigForge-Setup-<version>-win-x64.exe.
    This script is for a machine where the repository is already cloned and PowerShell is allowed to run.
    The daemon runs as a logon task instead of a Windows service because DXGI Desktop Duplication
    (Ambilight) only works inside the interactive user session. It runs elevated so that
    LibreHardwareMonitor can use the PawnIO driver for CPU temperatures and the Super I/O chip.
    The task stays enabled with on-demand start. -Autostart $false disables only the logon trigger.

.EXAMPLE
    .\deploy\windows\Install-RigForgeTask.ps1

.EXAMPLE
    .\deploy\windows\Install-RigForgeTask.ps1 -Autostart $false -CreateShortcuts
#>
param(
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'RigForge'),
    [string] $TaskName = 'RigForge',
    [bool] $Autostart = $true,
    [switch] $CreateShortcuts,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$repository = Resolve-Path (Join-Path $PSScriptRoot '..\..')

function Copy-RigForgeShell([string] $Repository, [string] $Destination) {
    $unpacked = Join-Path $Repository 'shell\dist\win-unpacked'
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'RigForge.exe'))) {
        throw "RigForge.exe was not produced at $unpacked."
    }

    Copy-Item -Path (Join-Path $unpacked '*') -Destination $Destination -Recurse -Force
}

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

function New-RigForgeShortcut([string] $ShortcutPath, [string] $Target) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($ShortcutPath)
    $link.TargetPath = $Target
    $link.WorkingDirectory = Split-Path -Parent $Target
    $link.IconLocation = "$Target,0"
    $link.Description = 'RigForge'
    $link.Save()
}

# Ask the daemon to hand fans and the AIO screen back to firmware before files are replaced.
Request-RigForgeShutdown
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Get-Process -Name 'RigForge.Daemon', 'RigForge', 'rigforge-scan' -ErrorAction SilentlyContinue | Stop-Process -Force

if (-not $SkipPublish) {
    Push-Location (Join-Path $repository 'ui')
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed in ui/.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
    }
    finally {
        Pop-Location
    }

    dotnet publish (Join-Path $repository 'src\Host.Daemon\RigForge.Host.Daemon.csproj') `
        -c Release -r win-x64 --self-contained false -o $InstallDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Daemon publish failed.' }

    Push-Location (Join-Path $repository 'shell')
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed in shell/.' }
        npm run dist
        if ($LASTEXITCODE -ne 0) { throw 'Electron shell build failed.' }
    }
    finally {
        Pop-Location
    }

    Copy-RigForgeShell -Repository $repository -Destination $InstallDirectory
}

$executable = Join-Path $InstallDirectory 'RigForge.Daemon.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "RigForge.Daemon.exe not found in $InstallDirectory."
}

$autostartText = 'false'
if ($Autostart) { $autostartText = 'true' }

$taskRegistered = $false
& $executable --install-task --autostart $autostartText
if ($LASTEXITCODE -eq 0) {
    $taskRegistered = $true
}

if (-not $taskRegistered) {
    Write-Host 'RigForge.Daemon.exe --install-task did not succeed; registering the task directly.'
    $user = "$env:USERDOMAIN\$env:USERNAME"
    $action = New-ScheduledTaskAction -Execute $executable -WorkingDirectory $InstallDirectory
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $trigger.Enabled = [bool] $Autostart
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    $settings.AllowDemandStart = $true
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings `
        -Description 'RigForge: RGB, fan/pump and AIO screen daemon' -Force | Out-Null
}

if ($CreateShortcuts) {
    $shellExe = Join-Path $InstallDirectory 'RigForge.exe'
    if (-not (Test-Path -LiteralPath $shellExe)) {
        throw "RigForge.exe was not found in $InstallDirectory."
    }

    $desktop = [Environment]::GetFolderPath('Desktop')
    $programs = [Environment]::GetFolderPath('Programs')
    New-RigForgeShortcut -ShortcutPath (Join-Path $desktop 'RigForge.lnk') -Target $shellExe
    New-RigForgeShortcut -ShortcutPath (Join-Path $programs 'RigForge.lnk') -Target $shellExe
}

Start-ScheduledTask -TaskName $TaskName

Write-Host "RigForge is running (autostart=$autostartText). Open the dashboard from the notification area icon, or at http://127.0.0.1:5757/#/dashboard."
Write-Host "Close NZXT CAM, SignalRGB and Armoury Crate's LightingService: they fight over the same controllers."
