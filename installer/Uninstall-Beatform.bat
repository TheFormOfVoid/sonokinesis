@echo off
rem Double-click to remove the Beatform loader. Your custom levels are kept.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Beatform.ps1" %*
pause
