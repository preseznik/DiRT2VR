@echo off
setlocal
cd /d "%~dp0..\..\.."
if not defined DIRT2VR_VS_ROOT set "DIRT2VR_VS_ROOT=C:\Program Files\Microsoft Visual Studio\2022\Community"
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
if not defined DIRT2VR_PYTHON set "DIRT2VR_PYTHON=python"
for %%R in (host client) do (
    "%DIRT2VR_PYTHON%" -B tools\lan\direct\provider_contract.py init %%R
    if errorlevel 1 exit /b 1
    cmake -S tools/lan/direct -B build/direct-%%R -G Ninja -DROLE=%%R -DCMAKE_BUILD_TYPE=Release
    if errorlevel 1 exit /b 1
    cmake --build build/direct-%%R --target XLiveLessNess -j 6
    if errorlevel 1 exit /b 1
    "%DIRT2VR_PYTHON%" -B tools\lan\direct\provider_contract.py bind %%R
    if errorlevel 1 exit /b 1
    cmake --build build/direct-%%R --target XLiveLessNess -j 6
    if errorlevel 1 exit /b 1
    "%DIRT2VR_PYTHON%" -B tools\lan\direct\provider_contract.py verify %%R
    if errorlevel 1 exit /b 1
)
