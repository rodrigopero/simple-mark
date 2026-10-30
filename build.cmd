@echo off
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4.x compiler not found.
  exit /b 1
)

if not exist "%~dp0bin" mkdir "%~dp0bin"

"%CSC%" /nologo /target:winexe /optimize+ /warnaserror+ /platform:anycpu ^
  /out:"%~dp0bin\MdViewer.exe" ^
  /win32manifest:"%~dp0src\app.manifest" ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "%~dp0src\*.cs"
