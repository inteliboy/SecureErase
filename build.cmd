@echo off
setlocal EnableExtensions

rem ============================================================================
rem  build.cmd - compile SecureErase.exe with the .NET Framework csc.exe.
rem  No MSBuild, no NuGet, no Visual Studio required. Works on full Windows and
rem  inside a WinPE image that carries the .NET Framework runtime + csc.exe.
rem
rem  Usage:
rem     build.cmd            auto-detect arch from the CURRENT machine
rem     build.cmd x86        force a 32-bit binary (for 32-bit WinPE)
rem     build.cmd x64        force a 64-bit binary (for 64-bit WinPE)
rem
rem  IMPORTANT: the binary's architecture must match the Windows/WinPE it runs
rem  on. Running an x64 exe on 32-bit WinPE gives "unsupported 16-bit
rem  application". If unsure, run this script INSIDE the target WinPE so it
rem  auto-detects correctly, or pass x86 / x64 explicitly.
rem ============================================================================

set "SRC=SecureErase.cs"
set "OUT=SecureErase.exe"
set "MANIFEST=app.manifest"

rem --- Determine target platform --------------------------------------------
set "PLAT=%~1"
if /I "%PLAT%"=="x86" goto :have_plat
if /I "%PLAT%"=="x64" goto :have_plat

rem No/invalid arg: auto-detect from the current process/OS architecture.
set "PLAT=x64"
if /I "%PROCESSOR_ARCHITECTURE%"=="x86" if not defined PROCESSOR_ARCHITEW6432 set "PLAT=x86"

:have_plat

rem --- Choose framework dir + csc.exe for that platform ----------------------
if /I "%PLAT%"=="x86" (
    set "NETDIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
) else (
    set "NETDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
)

set "CSC=%NETDIR%\csc.exe"
if not exist "%CSC%" (
    rem Either csc for that arch is missing; fall back to whichever exists.
    if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    ) else (
        set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
)
if not exist "%CSC%" (
    echo ERROR: csc.exe not found under %WINDIR%\Microsoft.NET.
    echo        Install the .NET Framework 4.x, or copy csc.exe next to this script.
    exit /b 1
)

set "MANIFESTOPT="
if exist "%MANIFEST%" set "MANIFESTOPT=/win32manifest:%MANIFEST%"

echo Compiler : %CSC%
echo Platform : %PLAT%
echo.

"%CSC%" /nologo /langversion:5 /optimize+ /target:exe /platform:%PLAT% ^
    %MANIFESTOPT% /out:"%OUT%" "%SRC%"

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    exit /b 1
)

echo.
echo BUILD OK -^> %OUT%  (%PLAT%)
echo Run inside a matching-arch WinPE:  %OUT% list
endlocal