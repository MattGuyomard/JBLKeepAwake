@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Build failed: the Windows .NET Framework C# compiler was not found.
    exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 /win32icon:app.ico /win32manifest:app.manifest /out:dist\JBLKeepAwake.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll JBLKeepAwake.cs KeepAwakeSignal.cs
if errorlevel 1 exit /b 1
echo Built dist\JBLKeepAwake.exe
