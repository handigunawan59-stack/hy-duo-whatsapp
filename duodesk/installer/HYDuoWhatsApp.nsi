Unicode True
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"

!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR must point to the published application folder"
!endif
!ifndef SETUP_OUT
  !error "SETUP_OUT must be the full output path for the installer"
!endif
!ifndef APP_NAME
  !define APP_NAME "HY Duo WhatsApp"
!endif
!ifndef APP_REGISTRY_KEY
  !define APP_REGISTRY_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HYDuoWhatsApp"
!endif
!ifndef CHECK_PROCESS_NAME
  !define CHECK_PROCESS_NAME "DuoDesk"
!endif

Name "${APP_NAME}"
OutFile "${SETUP_OUT}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey HKCU "${APP_REGISTRY_KEY}" "InstallLocation"
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} installer"
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "LegalCopyright" "Copyright 2026 HY Duo WhatsApp contributors"

!define MUI_ABORTWARNING
!define MUI_ICON "..\duodesk.ico"
!define MUI_UNICON "..\duodesk.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\DuoDesk.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Buka ${APP_NAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Indonesian"

; The current-user-only installer never closes a running app without permission.
; This also catches the older DuoDesk build, which predates the single-instance lock.
Function CheckAppClosed
  check_again:
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command "if (Get-Process -Name ${CHECK_PROCESS_NAME} -ErrorAction SilentlyContinue) { exit 10 } else { exit 0 }"'
  Pop $0
  Pop $1
  StrCmp $0 "0" done
  StrCmp $0 "10" running failed
  running:
    IfSilent failed
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Tutup DuoDesk atau ${APP_NAME} sepenuhnya melalui ikon di area notifikasi Windows, kemudian pilih Coba Lagi. Login dan data akun akan dipertahankan." IDRETRY check_again
    Abort
  failed:
    SetErrorLevel 1
    Abort
  done:
FunctionEnd

Function .onInit
  Call CheckAppClosed
FunctionEnd

Function un.CheckAppClosed
  un_check_again:
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command "if (Get-Process -Name ${CHECK_PROCESS_NAME} -ErrorAction SilentlyContinue) { exit 10 } else { exit 0 }"'
  Pop $0
  Pop $1
  StrCmp $0 "0" un_done
  StrCmp $0 "10" un_running un_failed
  un_running:
    IfSilent un_failed
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Tutup ${APP_NAME} sepenuhnya melalui ikon di area notifikasi Windows sebelum menghapusnya." IDRETRY un_check_again
    Abort
  un_failed:
    SetErrorLevel 1
    Abort
  un_done:
FunctionEnd

Function un.onInit
  Call un.CheckAppClosed
FunctionEnd

Section "Aplikasi" SEC_MAIN
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}\DuoDesk.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\DuoDesk.exe"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\DuoDesk.exe"
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "DisplayIcon" "$INSTDIR\DuoDesk.exe"
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${APP_REGISTRY_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${APP_REGISTRY_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${APP_REGISTRY_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"
  Delete "$INSTDIR\DuoDesk.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${APP_REGISTRY_KEY}"
  ; The account profiles and preferences are deliberately kept in AppData\Local\DuoDesk.
SectionEnd
