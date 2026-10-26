@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"
title Locifab 网页版
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Web.ps1"
echo.
echo 后端已启动。关闭服务可运行“停止网页版.cmd”。
timeout /t 3 /nobreak >nul
