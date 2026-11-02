@echo off
setlocal
rem EasyCapture: build the latest source (Release) and launch the tray app.

cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet was not found in PATH.
    echo         Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0
    echo         or run: winget install Microsoft.DotNet.SDK.9
    pause
    exit /b 1
)

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
echo.
echo EasyCapture started. It is a tray app, so no window is shown - check the system tray icon.
exit /b 0
