@echo off
setlocal
rem EasyCapture: close a running instance, build the latest source (Release), and launch the tray app.

cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet was not found in PATH.
    echo         Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0
    echo         or run: winget install Microsoft.DotNet.SDK.9
    pause
    exit /b 1
)

rem A running EasyCapture.exe locks the build output (MSB3021/MSB3027), and the single-instance
rem mutex would make a newly started process exit silently. Close it first. Only this image name is targeted.
call :is_running
if errorlevel 1 goto :build

echo EasyCapture is running. Closing it before the build...
rem Graceful request first (WM_CLOSE). A tray-only app has no window to close, so it usually stays alive; wait 2 seconds, then force.
taskkill /IM EasyCapture.exe >nul 2>&1
if not errorlevel 1 call :wait_exit 2
call :is_running
if errorlevel 1 goto :closed

taskkill /F /IM EasyCapture.exe >nul 2>&1
call :wait_exit 3
call :is_running
if errorlevel 1 goto :closed

echo.
echo [ERROR] Could not close EasyCapture. Close it from the tray or Task Manager, then run again.
pause
exit /b 1

:closed
echo EasyCapture closed.

:build
echo Building EasyCapture (Release)...
dotnet build "src\EasyCapture.App\EasyCapture.App.csproj" -c Release -v q
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Check the messages above and fix the errors, then run again.
    pause
    exit /b 1
)

set "APP_EXE=%~dp0src\EasyCapture.App\bin\Release\net9.0-windows\EasyCapture.exe"
if not exist "%APP_EXE%" (
    echo.
    echo [ERROR] Executable not found:
    echo         %APP_EXE%
    pause
    exit /b 1
)

start "" "%APP_EXE%"

rem Wait about 2 seconds, then confirm the new process is still alive. Warning only; the exit code is not changed.
ping -n 3 127.0.0.1 >nul
call :is_running
if errorlevel 1 (
    echo.
    echo [WARN] EasyCapture exited right after start. Run it again or check the tray.
    pause
    exit /b 0
)

echo.
echo EasyCapture started. It is a tray app, so no window is shown - check the system tray icon.
exit /b 0

rem ---- subroutines ----

rem Sets errorlevel 0 if EasyCapture.exe is running, 1 if not. Matches the image name only (no localized text).
:is_running
tasklist /FI "IMAGENAME eq EasyCapture.exe" /NH 2>nul | find /I "EasyCapture.exe" >nul
exit /b %errorlevel%

rem Waits up to %1 seconds (1 second steps) until EasyCapture.exe is gone.
:wait_exit
set "WAITS=%~1"
:wait_exit_loop
call :is_running
if errorlevel 1 exit /b 0
if %WAITS% leq 0 exit /b 0
set /a WAITS-=1
ping -n 2 127.0.0.1 >nul
goto :wait_exit_loop
