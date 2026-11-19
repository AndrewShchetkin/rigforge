; RigForge assisted NSIS hooks for electron-builder 26.18.0.
; Page hooks match assistedInstaller.nsh (customPageAfterChangeDir, customUnWelcomePage).
; customCheckAppRunning replaces CHECK_APP_RUNNING, which installSection.nsh runs
; before installApplicationFiles and which uninstaller.nsh runs before files are removed.
; ${isUpdated} is electron-builder's --updated flag (StdUtils.TestParameter).
; Install and uninstall call RigForge.Daemon.exe. They do not run PowerShell.

!include "LogicLib.nsh"
!include "nsDialogs.nsh"

!ifndef BUILD_UNINSTALLER
  Var RigForgeAutostart
  Var RigForgeDesktop
  Var RigForgeAutostartCheckbox
  Var RigForgeDesktopCheckbox
!else
  Var RigForgePurge
  Var RigForgePurgeCheckbox
!endif

!macro rigforgeGracefulStop
  ; Ask a daemon that is already running to restore fans and exit.
  ; Do not launch $INSTDIR\RigForge.Daemon.exe here: an older build treats
  ; unknown arguments as a normal start and never exits, so the installer waits forever.
  DetailPrint "Stopping RigForge so fans and the AIO screen can return to firmware"
  ${If} ${FileExists} "$SYSDIR\curl.exe"
    nsExec::ExecToLog `"$SYSDIR\curl.exe" --max-time 5 --connect-timeout 2 -sS -o NUL -X POST http://127.0.0.1:5757/api/host/shutdown`
    Pop $0
    Sleep 3000
  ${EndIf}
  nsExec::ExecToLog `"$SYSDIR\schtasks.exe" /End /TN "RigForge"`
  Pop $0
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "RigForge.exe"`
  Pop $0
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "RigForge.Daemon.exe"`
  Pop $0
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "rigforge-scan.exe"`
  Pop $0
  Sleep 400
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "RigForge.exe"`
  Pop $0
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "RigForge.Daemon.exe"`
  Pop $0
  nsExec::ExecToLog `"$SYSDIR\taskkill.exe" /F /IM "rigforge-scan.exe"`
  Pop $0
!macroend

!macro rigforgeDeleteDesktopShortcut
  Push $2
  StrCpy $2 "$installMode"
  SetShellVarContext all
  Delete "$DESKTOP\RigForge.lnk"
  SetShellVarContext current
  Delete "$DESKTOP\RigForge.lnk"
  ${If} $2 == "all"
    SetShellVarContext all
  ${Else}
    SetShellVarContext current
  ${EndIf}
  Pop $2
  System::Call 'shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'
!macroend

!macro rigforgeDeleteInstalledShortcuts
  Push $2
  StrCpy $2 "$installMode"
  SetShellVarContext all
  Delete "$DESKTOP\RigForge.lnk"
  Delete "$SMPROGRAMS\RigForge.lnk"
  RMDir "$SMPROGRAMS\RigForge"
  SetShellVarContext current
  Delete "$DESKTOP\RigForge.lnk"
  Delete "$SMPROGRAMS\RigForge.lnk"
  RMDir "$SMPROGRAMS\RigForge"
  ${If} $2 == "all"
    SetShellVarContext all
  ${Else}
    SetShellVarContext current
  ${EndIf}
  Pop $2
  System::Call 'shell32::SHChangeNotify(i 0x8000000, i 0, i 0, i 0)'
!macroend

; Runs from installSection before files are copied, and from the uninstaller before removal.
!macro customCheckAppRunning
  !insertmacro rigforgeGracefulStop
!macroend

!macro customInit
  StrCpy $RigForgeAutostart "1"
  StrCpy $RigForgeDesktop "1"

  ClearErrors
  ReadRegStr $0 HKLM "Software\RigForge" "AutostartOnSignIn"
  ${If} $0 == "0"
    StrCpy $RigForgeAutostart "0"
  ${ElseIf} $0 == "1"
    StrCpy $RigForgeAutostart "1"
  ${EndIf}

  ${StdUtils.GetParameter} $1 "AUTOSTART" ""
  ${If} $1 == "0"
    StrCpy $RigForgeAutostart "0"
  ${ElseIf} $1 == "1"
    StrCpy $RigForgeAutostart "1"
  ${EndIf}

  ${StdUtils.GetParameter} $1 "NODESKTOP" ""
  ${If} $1 == "1"
    StrCpy $RigForgeDesktop "0"
  ${EndIf}
!macroend

!macro customPageAfterChangeDir
  Page custom rigforgeOptionsCreate rigforgeOptionsLeave

  Function rigforgeOptionsCreate
    !insertmacro MUI_HEADER_TEXT "Installation options" "Choose sign-in startup and shortcuts."
    nsDialogs::Create 1018
    Pop $0

    ${NSD_CreateLabel} 0 0 100% 36u "The hardware daemon stays in the notification area so cooling curves, the AIO screen, and RGB keep running without UAC prompts. You can change this later from the tray icon or from Settings."
    Pop $0

    ${NSD_CreateCheckbox} 0 42u 100% 14u "Start RigForge automatically when I sign in to Windows (Recommended)"
    Pop $RigForgeAutostartCheckbox
    ${If} $RigForgeAutostart == "1"
      ${NSD_Check} $RigForgeAutostartCheckbox
    ${EndIf}

    ${NSD_CreateCheckbox} 0 64u 100% 14u "Create a Desktop shortcut"
    Pop $RigForgeDesktopCheckbox
    ${If} $RigForgeDesktop == "1"
      ${NSD_Check} $RigForgeDesktopCheckbox
    ${EndIf}

    ${NSD_CreateLabel} 16u 82u 100% 12u "A Start menu shortcut is always created."
    Pop $0

    nsDialogs::Show
  FunctionEnd

  Function rigforgeOptionsLeave
    ${NSD_GetState} $RigForgeAutostartCheckbox $0
    ${If} $0 == ${BST_CHECKED}
      StrCpy $RigForgeAutostart "1"
    ${Else}
      StrCpy $RigForgeAutostart "0"
    ${EndIf}

    ${NSD_GetState} $RigForgeDesktopCheckbox $0
    ${If} $0 == ${BST_CHECKED}
      StrCpy $RigForgeDesktop "1"
    ${Else}
      StrCpy $RigForgeDesktop "0"
    ${EndIf}
  FunctionEnd
!macroend

!macro customInstall
  !insertmacro rigforgeGracefulStop

  WriteRegStr HKLM "Software\RigForge" "AutostartOnSignIn" "$RigForgeAutostart"

  ${If} $RigForgeAutostart == "1"
    StrCpy $1 "true"
  ${Else}
    StrCpy $1 "false"
  ${EndIf}

  DetailPrint "Registering scheduled task \RigForge (autostart=$1)"
  nsExec::ExecToLog `"$INSTDIR\RigForge.Daemon.exe" --install-task --autostart $1`
  Pop $0

  ${If} $RigForgeDesktop != "1"
    !insertmacro rigforgeDeleteDesktopShortcut
  ${EndIf}

  ; Interactive setup starts the tray daemon even when sign-in autostart is off.
  ; Silent setup starts it only when autostart was requested.
  ${If} $RigForgeAutostart == "1"
  ${OrIfNot} ${Silent}
    DetailPrint "Starting RigForge"
    nsExec::ExecToLog `"$SYSDIR\schtasks.exe" /Run /TN "RigForge"`
    Pop $0
  ${EndIf}
!macroend

!macro customUnInit
  StrCpy $RigForgePurge "0"
  ${IfNot} ${isUpdated}
    ${StdUtils.GetParameter} $0 "PURGE" ""
    ${If} $0 == "1"
      StrCpy $RigForgePurge "1"
    ${EndIf}
  ${EndIf}
!macroend

!macro customUnWelcomePage
  UninstPage custom un.rigforgeUnCreate un.rigforgeUnLeave

  Function un.rigforgeUnCreate
    ${If} ${isUpdated}
      Abort
    ${EndIf}

    !insertmacro MUI_HEADER_TEXT "Uninstall RigForge" "Saved profiles are kept unless you choose to delete them."
    nsDialogs::Create 1018
    Pop $0

    ${NSD_CreateLabel} 0 0 100% 40u "Your profiles, fan curves, layouts, and custom effects stay in %LOCALAPPDATA%\RigForge. Leave the box below unchecked to keep them."
    Pop $0

    ${NSD_CreateCheckbox} 0 48u 100% 28u "Also delete my saved profiles, fan curves, layouts, and custom effects (%LOCALAPPDATA%\RigForge)"
    Pop $RigForgePurgeCheckbox
    ${If} $RigForgePurge == "1"
      ${NSD_Check} $RigForgePurgeCheckbox
    ${Else}
      ${NSD_Uncheck} $RigForgePurgeCheckbox
    ${EndIf}

    nsDialogs::Show
  FunctionEnd

  Function un.rigforgeUnLeave
    ${NSD_GetState} $RigForgePurgeCheckbox $0
    ${If} $0 == ${BST_CHECKED}
      StrCpy $RigForgePurge "1"
    ${Else}
      StrCpy $RigForgePurge "0"
    ${EndIf}
  FunctionEnd
!macroend

!macro customUnInstall
  ; An in-place upgrade passes --updated. Stop processes so files can be replaced,
  ; but never delete the task for good and never touch %LOCALAPPDATA%\RigForge.
  ${If} ${isUpdated}
    !insertmacro rigforgeGracefulStop
  ${Else}
    !insertmacro rigforgeGracefulStop

    DetailPrint "Removing scheduled task \RigForge"
    nsExec::ExecToLog `"$INSTDIR\RigForge.Daemon.exe" --uninstall-task`
    Pop $0
    nsExec::ExecToLog `"$SYSDIR\schtasks.exe" /Delete /TN "RigForge" /F`
    Pop $0

    Delete "$oldDesktopLink"
    Delete "$oldStartMenuLink"
    !insertmacro rigforgeDeleteInstalledShortcuts

    Push $2
    StrCpy $2 "$installMode"
    SetShellVarContext current
    RMDir /r "$APPDATA\RigForge"
    ${If} $RigForgePurge == "1"
      RMDir /r "$LOCALAPPDATA\RigForge"
    ${Else}
      RMDir /r "$LOCALAPPDATA\RigForge\app"
    ${EndIf}
    ${If} $2 == "all"
      SetShellVarContext all
    ${Else}
      SetShellVarContext current
    ${EndIf}
    Pop $2

    DeleteRegKey HKLM "Software\RigForge"
  ${EndIf}
!macroend
