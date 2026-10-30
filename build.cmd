@echo off
setlocal

set "ROOT=%~dp0"
set "LIB=%ROOT%lib"
set "OUT=%ROOT%bin"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4.x compiler not found.
  exit /b 1
)

rem Refuse to build if any vendored library differs from its pinned SHA-256.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "Get-Content -LiteralPath '%LIB%\SHA256SUMS' | ForEach-Object {" ^
  "  $hash, $file = $_ -split '\s+', 2;" ^
  "  $sha = [Security.Cryptography.SHA256]::Create();" ^
  "  $bytes = $sha.ComputeHash([IO.File]::ReadAllBytes((Join-Path '%LIB%' $file)));" ^
  "  $sha.Dispose();" ^
  "  $actual = -join ($bytes | ForEach-Object { $_.ToString('x2') });" ^
  "  if ($actual -ne $hash.ToLowerInvariant()) { throw ('Hash mismatch: ' + $file) }" ^
  "}"
if errorlevel 1 (
  echo Library verification failed.
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /target:winexe /optimize+ /warnaserror+ /platform:anycpu ^
  /out:"%OUT%\SimpleMark.exe" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /win32icon:"%ROOT%src\app.ico" ^
  /resource:"%ROOT%src\app.ico",SimpleMark.app.ico ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:"%LIB%\Markdig.dll" /r:"%LIB%\System.Memory.dll" ^
  "%ROOT%src\*.cs"
if errorlevel 1 exit /b 1

copy /y "%LIB%\*.dll" "%OUT%\" >nul
