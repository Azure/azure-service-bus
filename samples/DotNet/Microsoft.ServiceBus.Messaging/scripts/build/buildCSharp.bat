@echo off
set VSTOOLS_PATH="%VS140COMNTOOLS%"
goto :LOADVSTOOLS

set VSTOOLS_PATH="%ProgramFiles(x86)%\Microsoft Visual Studio 14.0\Common7\Tools\"
goto :LOADVSTOOLS

set VSTOOLS_PATH="%VS120COMNTOOLS%"
goto :LOADVSTOOLS

set VSTOOLS_PATH="%ProgramFiles(x86)%\Microsoft Visual Studio 12.0\Common7\Tools\"
goto :LOADVSTOOLS

set VSTOOLS_PATH="%VS110COMNTOOLS%"
goto :LOADVSTOOLS

set VSTOOLS_PATH="%ProgramFiles(x86)%\Microsoft Visual Studio 11.0\Common7\Tools\"
goto :LOADVSTOOLS

set WARN_MESSAGE=WARNING: Could not locate Visual Studio 2012 or Visual Studio 2013 build tools. Your build may fail while trying to call msbuild.exe if its not in your system path.
powershell -Command Write-Host "%WARN_MESSAGE%" -foreground "Yellow"

goto BUILD

:LOADVSTOOLS
IF EXIST %VSTOOLS_PATH%vsvars32.bat (
    echo Loading Visual Studio build tools from %VSTOOLS_PATH%
    call %VSTOOLS_PATH%vsvars32.bat
    goto BUILD
)
goto :eof

:BUILD
if [%1] NEQ [] (pushd %1)
echo.
PowerShell.exe -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; & '%~dp0buildCSharp.ps1'; EXIT $LASTEXITCODE }"
IF %ERRORLEVEL% NEQ 0 (
    echo build.ps1 returned non-zero exit code: %ERRORLEVEL%. Please ensure build completes successfully before you can run the examples.
    goto ERROR
)

goto DONE
goto :eof

:ERROR
if [%1] NEQ [] (popd)
if %ERRORLEVEL% NEQ 0 (
exit /b %ERRORLEVEL%
) ELSE (
exit /b -1
)

:DONE
if [%1] NEQ [] (popd)
exit /b %ERRORLEVEL%
