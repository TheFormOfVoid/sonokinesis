@echo off
rem Drop level .zip files or level folders onto this file to add them to the game.
rem Double-click it to open your levels folder.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Add-Level.ps1" %*
pause
