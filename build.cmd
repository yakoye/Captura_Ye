@echo off
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\release.ps1" -AutoVersion %*
set "build_exit=%ERRORLEVEL%"
pause
exit /b %build_exit%
