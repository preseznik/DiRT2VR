@echo off
setlocal
cd /d "%~dp0..\.."
if not defined DIRT2VR_VS_ROOT set "DIRT2VR_VS_ROOT=C:\Program Files\Microsoft Visual Studio\2022\Community"
call "%DIRT2VR_VS_ROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
if not exist build\driving-input mkdir build\driving-input
cl /nologo /std:c++20 /EHsc /W4 /O2 /MT /LD /DUNICODE /D_UNICODE /DNOMINMAX tools\driving-input\input.cpp /Fobuild\driving-input\input.obj /Febuild\driving-input\driving_input.dll /link dinput8.lib dxguid.lib xinput.lib ole32.lib user32.lib
exit /b %errorlevel%
