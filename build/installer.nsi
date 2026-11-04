; Project Focus Manager — 传统 Next-Next-Finish 安装器（NSIS 3）
; 复用 dist/win-x64 的自包含发布产物，不重新编译程序。
; 冒烟测试变体: makensis -DSMOKE build\installer.nsi （用户级、输出到 .tools\temp）
Unicode true
ManifestDPIAware true

!define APPNAME "Project Focus Manager"
!define APPID "ProjectFocusManager"
!define EXE "ProjectFocusManager.exe"
!define UNKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"

!ifdef SMOKE
  OutFile "..\.tools\temp\nsis-smoke.exe"
  RequestExecutionLevel user
!else
  OutFile "..\dist\Installer-traditional\${APPID}-1.1.0-Setup.exe"
  RequestExecutionLevel admin
!endif

!define MUI_ICON "..\src\FocusManager.App\Resources\app.ico"
!define MUI_UNICON "..\src\FocusManager.App\Resources\app.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"

!include "MUI2.nsh"

Name "${APPNAME}"
VIProductVersion "1.1.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APPNAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "1.1.0"
VIAddVersionKey /LANG=2052 "ProductVersion" "1.1.0"
VIAddVersionKey /LANG=2052 "LegalCopyright" "monoshy"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

InstallDir "$PROGRAMFILES64\${APPID}"
ShowInstDetails show
ShowUnInstDetails show

Section "Install"
  SetShellVarContext all
  SetOutPath "$INSTDIR"
  ; data 是本机运行产生的数据库，绝不能打进安装包
  File /r /x data /x *.db /x *.db-wal /x *.db-shm /x *.log "..\dist\win-x64\*.*"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXE}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXE}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${UNKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNKEY}" "DisplayVersion" "1.1.0"
  WriteRegStr HKLM "${UNKEY}" "Publisher" "monoshy"
  WriteRegStr HKLM "${UNKEY}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKLM "${UNKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNKEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegDWORD HKLM "${UNKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\*.*"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNKEY}"
  ; 用户数据保留在 %LOCALAPPDATA%\ProjectFocusManager\data，卸载不清除
SectionEnd
