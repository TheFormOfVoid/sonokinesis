@echo off
rem Double-click to install or update the Beatform loader.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Beatform.ps1" %*
pause
