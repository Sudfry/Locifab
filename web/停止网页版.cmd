@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Stop-Web.ps1"
echo 已尝试停止 Locifab 网页版后端和时间属性桥接。
timeout /t 2 /nobreak >nul
