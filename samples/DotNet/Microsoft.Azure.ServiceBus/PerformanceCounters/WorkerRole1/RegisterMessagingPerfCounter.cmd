@echo off
set currentDir=%CD%
echo Unregistering Microsoft.ServiceBus.MessagingPerformanceCounters.man
"%WinDir%\System32\unlodctr.exe" /m:Performance\Microsoft.ServiceBus.MessagingPerformanceCounters.man

echo Registering Microsoft.ServiceBus.MessagingPerformanceCounters.man
"%WinDir%\System32\lodctr.exe" /M:Performance\Microsoft.ServiceBus.MessagingPerformanceCounters.man "%CD%"
IF %ERRORLEVEL% NEQ 0 (
	echo 
	echo Registration failed. error level is %ERRORLEVEL%
	echo %ERRORLEVEL%
	exit /b %ERRORLEVEL%
)