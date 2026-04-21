@echo off
REM Paint.NET Distortion Plugins Installer (Batch Wrapper)
REM This script launches the PowerShell installer

setlocal enabledelayedexpansion

echo Paint.NET Distortion Plugins Installer
echo ======================================

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Check if PowerShell script exists
if not exist "%SCRIPT_DIR%install-plugins.ps1" (
    echo ERROR: install-plugins.ps1 not found!
    echo Please ensure this .bat file is in the same directory as install-plugins.ps1
    pause
    exit /b 1
)

REM Run the PowerShell script
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%install-plugins.ps1"

REM Exit with the same code as PowerShell
exit /b %ERRORLEVEL%
