@echo off
setlocal
cd /d "%~dp0..\.."
if not defined DIRT2VR_VS_ROOT set "DIRT2VR_VS_ROOT=C:\Program Files\Microsoft Visual Studio\2022\Community"
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars32.bat"
if errorlevel 1 exit /b 1
cmake -S tools/lan -B build/lan -G Ninja -DCMAKE_BUILD_TYPE=Release "-DDXSDK_DIR=%CD%/.deps/dxsdk-jun10/DXSDK"
if errorlevel 1 exit /b 1
cmake --build build/lan -j 8
if errorlevel 1 exit /b 1
ctest --test-dir build/lan --output-on-failure
exit /b %errorlevel%
