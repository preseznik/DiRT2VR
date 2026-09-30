#include "shadow_mask.h"
#include "common.h"
#include <d3dcompiler.h>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
// Preserve the game's shadow-mask quad position/depth and texture coordinates.
// Keep the game's ray scale; add the off-centre terms omitted by its VS.
constexpr char source[]=R"(
cbuffer Globals : register(b0) { float4 screenProj; }
cbuffer EyeRays : register(b13) { float4 eyeRays; }
struct Output { float4 position:SV_Position; float4 coordinates:TEXCOORD0; };
Output main(float4 position:POSITION) {
    Output o;
    o.position=float4(position.xy,90.0/(-90.0*(screenProj.z-1.0)+screenProj.w),1.0);
    o.coordinates=float4(position.xy*float2(0.5,-0.5)+0.5,position.xy*screenProj.xy+eyeRays.zw);
    return o;
}
)";
}
bool ShadowMaskPass::Draw(ID3D11DeviceContext* context,uint64_t vertex,uint64_t pixel,const ShadowRays& rays,const std::function<void()>& draw) {
    if(!rays.valid || vertex!=0xda67bdeb4b619ec9ull ||
       (pixel!=0x0024789eedc9b7bcull && pixel!=0xc3d93558eae69b72ull && pixel!=0xccdb9d07dcf3406eull) ||
       context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) return false;
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(device_.Get()!=device.Get()) {device_=device;shader_.Reset();constants_.Reset();attempted_=false;}
    if(!attempted_) {
        attempted_=true;
        ComPtr<ID3DBlob> code,error;
        HRESULT hr=D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"main","vs_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error);
        if(SUCCEEDED(hr)) hr=device_->CreateVertexShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader_);
        D3D11_BUFFER_DESC desc{};desc.ByteWidth=16;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        if(SUCCEEDED(hr)) hr=device_->CreateBuffer(&desc,nullptr,&constants_);
        if(FAILED(hr)) {shader_.Reset();constants_.Reset();Log("Stereo shadow mask unavailable hr=0x%08x",unsigned(hr));}
        else Log("Stereo shadow mask view-ray correction initialized");
    }
    if(!shader_ || !constants_) return false;
    struct Restore {
        ID3D11DeviceContext* context;
        ComPtr<ID3D11VertexShader> shader;
        ComPtr<ID3D11Buffer> constants;
        ~Restore() {context->VSSetShader(shader.Get(),nullptr,0);auto buffer=constants.Get();context->VSSetConstantBuffers(13,1,&buffer);}
    } restore{context};
    context->VSGetShader(&restore.shader,nullptr,nullptr);
    context->VSGetConstantBuffers(13,1,&restore.constants);
    context->UpdateSubresource(constants_.Get(),0,nullptr,rays.values.data(),0,0);
    auto buffer=constants_.Get();context->VSSetConstantBuffers(13,1,&buffer);
    context->VSSetShader(shader_.Get(),nullptr,0);
    draw();return true;
}
}
