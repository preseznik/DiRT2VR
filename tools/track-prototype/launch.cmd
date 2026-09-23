@echo off
set "TrackRepo=%~dp0..\.."
set "TrackGame=%TrackRepo%\artifacts\track-prototype\game"
set "TrackLauncher=%TrackRepo%\artifacts\track-prototype\launcher\DiRT2VR.exe"
if not exist "%TrackLauncher%" (
  echo Build the Release launcher first. See docs\custom-tracks.md.
  exit /b 1
)
if not exist "%TrackGame%\tracks\london\d2vr_test\route_0\prototype.json" (
  echo Prepare and install the isolated prototype first. See docs\custom-tracks.md.
  exit /b 1
)
start "" "%TrackLauncher%" --game "%TrackGame%"
