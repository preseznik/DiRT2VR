@echo off
setlocal
cd /d "%~dp0.."
if not defined DIRT2VR_VS_ROOT set "DIRT2VR_VS_ROOT=C:\Program Files\Microsoft Visual Studio\2022\Community"
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars32.bat"
if errorlevel 1 exit /b 1
set "pythonOption="
if defined DIRT2VR_PYTHON set pythonOption=-DPython3_EXECUTABLE="%DIRT2VR_PYTHON%"
cmake -S . -B build\distribution -G Ninja -DCMAKE_BUILD_TYPE=Release -DDIRT2VR_STATIC_RUNTIME=ON %pythonOption%
if errorlevel 1 exit /b 1
cmake --build build\distribution -j 8
if errorlevel 1 exit /b 1
ctest --test-dir build\distribution --output-on-failure
exit /b %errorlevel%
