@echo off
pushd "%~dp0"
PowerShell.exe -ExecutionPolicy Bypass -Command "& { $ErrorActionPreference = 'Stop'; & '%~dp0setup.ps1'; EXIT $LASTEXITCODE }"
if %ERRORLEVEL% NEQ 0 (
    echo Build failed! Please check logs for error information.
)
popd
exit /b %ERRORLEVEL%