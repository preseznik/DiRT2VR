#include <windows.h>
#include <d3d11.h>
#include <filesystem>
#include <cstdio>
int wmain(int argc,wchar_t** argv) {
    if(argc!=2) return 1;
    auto output=std::filesystem::temp_directory_path()/(L"dirt2vr-inactive-"+std::to_wstring(GetCurrentProcessId()));
    if(std::filesystem::exists(output)) return 2;
    SetEnvironmentVariableW(L"DIRT2VR_ACTIVE",L"0");
    SetEnvironmentVariableW(L"DIRT2VR_HEADSET",L"1");
    SetEnvironmentVariableW(L"DIRT2VR_OUTPUT",output.c_str());
    auto library=LoadLibraryW(argv[1]);
    if(!library) return 3;
    auto create=reinterpret_cast<decltype(&D3D11CreateDevice)>(GetProcAddress(library,"D3D11CreateDevice"));
    ID3D11Device* device{}; ID3D11DeviceContext* context{};
    auto hr=create(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context);
    if(context) context->Release(); if(device) device->Release();
    bool clean=!std::filesystem::exists(output);
    FreeLibrary(library);
    if(FAILED(hr) || !clean) return 4;
    puts("Inactive proxy forwards device creation without creating diagnostics."); return 0;
}
