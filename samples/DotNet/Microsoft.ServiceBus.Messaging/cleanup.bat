@echo off
pushd "%~dp0"
PowerShell.exe -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; & '%~dp0cleanup.ps1'; EXIT $LASTEXITCODE }"
if %ERRORLEVEL% NEQ 0 (
    echo Cleanup failed! Please check logs for error information.
)
popd
exit /b %ERRORLEVEL%