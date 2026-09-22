@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Start-LanTest.ps1" -Configure %*
if errorlevel 1 pause
