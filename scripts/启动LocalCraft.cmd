@echo off
setlocal
set "LC_EXE=%~dp0LocalCraft.exe"

if not exist "%LC_EXE%" (
    echo 找不到 LocalCraft.exe。
    echo 请把本脚本和 LocalCraft.exe 放在同一个文件夹。
    pause
    exit /b 1
)

echo 正在清除下载锁定（Mark of the Web）...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath $env:LC_EXE -ErrorAction SilentlyContinue"

echo 正在启动 LocalCraft...
start "" "%LC_EXE%"

echo.
echo 如果 Windows 仍提示“Windows 已保护你的电脑”：
echo   1. 点击“更多信息”
echo   2. 点击“仍要运行”
echo.
echo 说明：本脚本只清除“来自 Internet”的文件标记，不会关闭 SmartScreen，
echo 也不会伪造数字签名。正式分发仍建议使用代码签名证书。
timeout /t 5 /nobreak >nul
endlocal
