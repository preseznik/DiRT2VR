@echo off
title DiRT 2 VR - keep this window open until the game exits
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run-trace.ps1" -Interactive
if errorlevel 1 pause
