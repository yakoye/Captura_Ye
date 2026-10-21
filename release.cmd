@echo off
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\release.ps1" -Publish -Archive -AutoVersion %*
set "release_exit=%ERRORLEVEL%"
pause
exit /b %release_exit%
