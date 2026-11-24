@echo off
setlocal
cd /d "%~dp0" || exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Build failed: the Windows .NET Framework C# compiler was not found.
    exit /b 1
)

if not exist "..\build\" mkdir "..\build"
if not exist "..\build\" exit /b 1
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 ^
    /win32icon:assets\app.ico /win32manifest:assets\app.manifest /out:..\build\JBLKeepAwake.exe ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    JBLKeepAwake.cs KeepAwakeSignal.cs AudioOutputs.cs SignalSettings.cs
if errorlevel 1 exit /b 1

move /y "..\build\JBLKeepAwake.exe" "..\JBLKeepAwake.exe" >nul
if errorlevel 1 (
    echo Could not replace JBLKeepAwake.exe. Exit the app from the tray and build again.
    exit /b 1
)
echo Built JBLKeepAwake.exe in the repository root.
