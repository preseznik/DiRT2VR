#include "menu_motion_blur.h"
#include "common.h"
using Microsoft::WRL::ComPtr;
namespace vr {
// Native frontend final pass (grid VS 051e97f9f805ee40, PS 56a138db8eaa2db3)
// blurs along t0 velocity, decoded as 2*sample-1 and scaled by motionBlurLength.
// The game derives velocity from the previously rendered camera, which in a
// headset frame is the other eye, so every pixel smears sideways. Bind a still
// velocity for this draw only; the twitch offset and colour grading are unchanged.
bool MenuMotionBlur::Draw(ID3D11DeviceContext* context,uint64_t vertex,uint64_t pixel,const std::function<void()>& draw) {
    if(vertex!=0x051e97f9f805ee40ull || pixel!=0x56a138db8eaa2db3ull ||
       context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    ComPtr<ID3D11ShaderResourceView> velocity;context->PSGetShaderResources(0,1,&velocity);
    if(!velocity)return false;
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(device_.Get()!=device.Get()){device_=device;still_.Reset();attempted_=false;}
    if(!attempted_) {
        attempted_=true;
        const uint16_t texel[2]{0x3800,0x3800}; // Exactly 0.5 in half floats: no motion.
        D3D11_TEXTURE2D_DESC desc{};desc.Width=desc.Height=desc.MipLevels=desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_R16G16_FLOAT;desc.SampleDesc.Count=1;
        desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{texel,sizeof(texel),0};
        ComPtr<ID3D11Texture2D> texture;
        auto hr=device->CreateTexture2D(&desc,&data,&texture);
        if(SUCCEEDED(hr))hr=device->CreateShaderResourceView(texture.Get(),nullptr,&still_);
        if(FAILED(hr)){still_.Reset();Log("VR menu motion blur correction unavailable hr=%08x",unsigned(hr));}
        else Log("VR menu motion blur correction initialized");
    }
    if(!still_)return false;
    struct Restore {
        ID3D11DeviceContext* context;
        ID3D11ShaderResourceView* view;
        ~Restore(){context->PSSetShaderResources(0,1,&view);}
    } restore{context,velocity.Get()};
    auto still=still_.Get();context->PSSetShaderResources(0,1,&still);
    draw();return true;
}
}
