#include "graphics_diagnostics.h"
#include <wrl/client.h>
#include <string>
#include <vector>
#include <stdexcept>

std::vector<std::string> messages;
void Require(bool ok,const char* message) { if(!ok) throw std::runtime_error(message); }
int main() {
    try {
        using namespace vr::graphics;
        Require(Failed(E_OUTOFMEMORY,nullptr,"disabled") && failures==0,"disabled logging must preserve failure and do no collection");
        logger=+[](const char* line) { messages.emplace_back(line); };
        Require(!Failed(S_OK,nullptr,"success") && messages.empty(),"successful frames must not log");
        Microsoft::WRL::ComPtr<ID3D11Device> device;
        Require(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr)),"WARP device failed");
        D3D11_TEXTURE2D_DESC desc{}; desc.Width=0; desc.Height=1200; desc.SampleDesc.Count=8;
        Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
        auto hr=device->CreateTexture2D(&desc,nullptr,&texture);
        Require(Failed(hr,device.Get(),"invalid texture",&desc),"real rejected allocation not reported");
        Require(messages.size()==2 && messages[0].find("samples=8")!=std::string::npos && messages[0].find("device_removed=0x00000000")!=std::string::npos,"missing texture/device context");
        Require(messages[1].find("largest_free=")!=std::string::npos && messages[1].find("complete=1")!=std::string::npos,"missing complete memory snapshot");
        for(unsigned i=0;i<100;++i) Failed(DXGI_ERROR_DEVICE_REMOVED,device.Get(),"Present");
        Require(messages.size()==33 && messages.back().find("capped")!=std::string::npos,"repeated failures must be bounded");
        messages.clear();
        for(unsigned i=0;i<100;++i) { Allocation allocation("test"); }
        Require(messages.size()==32 && messages.front().find("phase=before")!=std::string::npos && messages.back().find("phase=after")!=std::string::npos,"allocation snapshots must bracket work and be bounded");
        puts("graphics diagnostics passed"); return 0;
    } catch(const std::exception& error) { puts(error.what()); return 1; }
}
