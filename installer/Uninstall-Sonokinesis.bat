@echo off
rem Double-click to remove the Sonokinesis loader. Your custom levels are kept.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Sonokinesis.ps1" %*
pause
