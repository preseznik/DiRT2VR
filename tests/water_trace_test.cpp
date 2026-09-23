#include "water_trace.h"
#include "common.h"
#include <wrl/client.h>
#include <array>
#include <fstream>
#include <cstdio>
#include <cstdlib>
using Microsoft::WRL::ComPtr;
#define CHECK(x) do { if(!(x)) { std::fprintf(stderr,"Failed line %d: %s\n",__LINE__,#x); std::exit(1); } } while(0)
int main() {
    const auto output=std::filesystem::current_path()/("water-trace-test-"+std::to_string(GetCurrentProcessId()));
    SetEnvironmentVariableW(L"DIRT2VR_LOGGING",L"1"); SetEnvironmentVariableW(L"DIRT2VR_OUTPUT",output.c_str());
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    CHECK(SUCCEEDED(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_WARP,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context)));
    std::array<float,4> values{1,2,3,4};
    D3D11_BUFFER_DESC bd{}; bd.ByteWidth=16; bd.Usage=D3D11_USAGE_DEFAULT; bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    D3D11_SUBRESOURCE_DATA initial{values.data(),0,0}; ComPtr<ID3D11Buffer> buffer;
    CHECK(SUCCEEDED(device->CreateBuffer(&bd,&initial,&buffer)));
    auto cb=buffer.Get(); context->VSSetConstantBuffers(0,1,&cb); context->PSSetConstantBuffers(0,1,&cb);
    std::array<unsigned,16*16> pixels; pixels.fill(0xff123456);
    D3D11_TEXTURE2D_DESC td{}; td.Width=td.Height=16; td.MipLevels=td.ArraySize=1;
    td.Format=DXGI_FORMAT_R8G8B8A8_UNORM; td.SampleDesc.Count=1; td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    initial={pixels.data(),16*4,0}; ComPtr<ID3D11Texture2D> texture; ComPtr<ID3D11ShaderResourceView> view;
    CHECK(SUCCEEDED(device->CreateTexture2D(&td,&initial,&texture)));
    CHECK(SUCCEEDED(device->CreateShaderResourceView(texture.Get(),nullptr,&view)));
    auto srv=view.Get(); context->PSSetShaderResources(1,1,&srv);
    vr::TraceWaterInputs(context.Get(),0,7,1); CHECK(!std::filesystem::exists(output));
    for(unsigned eye=1;eye<=2;++eye) for(unsigned call=0;call<6;++call)
        vr::TraceWaterInputs(context.Get(),0x122478b22e69d9faull,7,eye);
    for(unsigned eye=1;eye<=2;++eye) {
        const auto prefix="water-7-eye-"+std::to_string(eye)+"-draw-";
        CHECK(std::filesystem::exists(output/(prefix+"3.txt")));
        CHECK(!std::filesystem::exists(output/(prefix+"4.txt")));
        for(auto stage:{"vs","ps"}) {
            std::array<float,4> read{};
            std::ifstream file(output/(prefix+"0-"+stage+"-cb-0.bin"),std::ios::binary);
            file.read(reinterpret_cast<char*>(read.data()),sizeof(read)); CHECK(read==values);
        }
        unsigned pixel{}; std::ifstream file(output/(prefix+"0-srv-1.bin"),std::ios::binary);
        file.read(reinterpret_cast<char*>(&pixel),4); CHECK(pixel==pixels[0]);
    }
    ComPtr<ID3D11Buffer> after; ComPtr<ID3D11ShaderResourceView> afterView;
    context->PSGetConstantBuffers(0,1,&after); context->PSGetShaderResources(1,1,&afterView);
    CHECK(after.Get()==buffer.Get() && afterView.Get()==view.Get());
    vr::TraceWaterInputs(context.Get(),0x122478b22e69d9faull,8,1);
    CHECK(std::filesystem::exists(output/"water-8-eye-1-draw-0.txt"));
    std::puts("Water GPU captures preserve bindings, read actual inputs, reject unknown shaders and enforce per-eye bounds.");
}
