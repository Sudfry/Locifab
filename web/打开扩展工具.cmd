@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"

netstat -ano | findstr ":9501 " >nul 2>&1
if errorlevel 1 (
    start "Locifab File Bridge" powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0FileBridge.ps1"
    timeout /t 1 /nobreak >nul
)

start "" "http://127.0.0.1:9501/"
