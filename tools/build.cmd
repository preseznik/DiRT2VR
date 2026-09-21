@echo off
setlocal
cd /d "%~dp0.."
if not defined DIRT2VR_VS_ROOT for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "DIRT2VR_VS_ROOT=%%i"
if not defined DIRT2VR_VS_ROOT (
  echo Visual Studio with the x86 C++ build tools is required.
  exit /b 1
)
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars32.bat"
if errorlevel 1 exit /b 1
set "pythonOption="
if defined DIRT2VR_PYTHON set pythonOption=-DPython3_EXECUTABLE="%DIRT2VR_PYTHON%"
cmake -S . -B build\ninja -G Ninja -DCMAKE_BUILD_TYPE=RelWithDebInfo %pythonOption%
if errorlevel 1 exit /b 1
cmake --build build\ninja -j 8
if errorlevel 1 exit /b 1
ctest --test-dir build\ninja --output-on-failure
exit /b %errorlevel%
