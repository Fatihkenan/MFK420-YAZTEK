@echo off
setlocal
chcp 65001 >nul
set "YAZTEK_ROOT=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%YAZTEK_ROOT%YAZTEK-BASLAT.ps1"
set "YAZTEK_EXIT=%ERRORLEVEL%"
if not "%YAZTEK_EXIT%"=="0" (
  echo.
  echo Baslatma tamamlanamadi. Ayrintilar yukarida ve .yaztek-launcher\logs klasorundedir.
  pause
)
exit /b %YAZTEK_EXIT%
