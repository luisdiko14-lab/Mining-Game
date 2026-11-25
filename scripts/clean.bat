@echo off
setlocal
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\clean.ps1"
exit /b %ERRORLEVEL%
