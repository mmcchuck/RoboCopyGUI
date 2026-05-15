@echo off
setlocal
pushd "%~dp0"

if not exist "RoboCopyGUI.ico" (
    echo Generating RoboCopyGUI.ico ...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\make-icon.ps1"
    if errorlevel 1 (
        echo Icon generation failed.
        popd & exit /b 1
    )
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found in %WINDIR%\Microsoft.NET\Framework[64]\v4.0.30319
    popd & exit /b 1
)

set "OUT=RoboCopyGUI.exe"

"%CSC%" ^
  /nologo ^
  /target:winexe ^
  /platform:anycpu ^
  /optimize+ ^
  /out:"%OUT%" ^
  /win32icon:RoboCopyGUI.ico ^
  /resource:RoboCopyGUI.ico,RoboCopyGUI.RoboCopyGUI.ico ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:Microsoft.VisualBasic.dll ^
  src\*.cs

set ERR=%ERRORLEVEL%
popd
exit /b %ERR%
