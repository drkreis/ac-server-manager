@echo off
if not exist "%~dp0artifacts\desktop-0.5.2\AssettoServerManager.exe" (
    echo Build the desktop app first with Build-Desktop.ps1
    pause
    exit /b 1
)
start "" "%~dp0artifacts\desktop-0.5.2\AssettoServerManager.exe"
