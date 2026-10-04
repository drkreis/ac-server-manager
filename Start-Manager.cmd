@echo off
if not exist "%~dp0artifacts\desktop-0.4.1\AssettoServerManager.exe" (
    echo Build the desktop app first with Build-Desktop.ps1
    pause
    exit /b 1
)
start "" "%~dp0artifacts\desktop-0.4.1\AssettoServerManager.exe"
