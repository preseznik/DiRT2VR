@echo off
setlocal
cd /d "%~dp0..\.."
if not defined DIRT2VR_VS_ROOT set "DIRT2VR_VS_ROOT=C:\Program Files\Microsoft Visual Studio\2022\Community"
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
rem x64 helper for DLSS 5 Neural Rendering in Photo Mode, plus its caller shim. No NVIDIA SDK is used.
cmake -S tools\desktop-bridge -B build\desktop-bridge -G Ninja -DCMAKE_BUILD_TYPE=Release -DDIRT2VR_BRIDGE_LAB=OFF
if errorlevel 1 exit /b 1
cmake --build build\desktop-bridge -j 8
exit /b %errorlevel%
