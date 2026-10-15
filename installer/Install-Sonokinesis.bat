@echo off
rem Double-click to install or update the Sonokinesis loader.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Sonokinesis.ps1" %*
pause
