#include "exposure_window.h"
#include "common.h"
#include <d3dcompiler.h>
#include <cmath>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace vr {
MeterWindow NativeMeterWindow(float x,float y,const float* p) {
    if(!p || !std::isfinite(x) || !std::isfinite(y) || x<=0 || y<=0 || p[11]!=-1 || p[15]!=0 || p[0]<=0 || p[5]<=0)return {};
    const float w=p[0]/x,h=p[5]/y;
    MeterWindow r{{(1-p[8]-w)*.5f,(1+p[9]-h)*.5f,w,h},true};
    for(float v:r.bounds)if(!std::isfinite(v))return {};
    if(r.bounds[0]<0 || r.bounds[1]<0 || r.bounds[0]+w>1.00001f || r.bounds[1]+h>1.00001f)return {};
    return r;
}
// Preserve the native 16-tap ALPHA luminance reduction,
// including its finite-value guard. Only change which angular region it meters.
// The bloom RGB chain, 4x4 reduction, adaptation shader and CPU damping stay native.
bool ExposureWindow::Draw(ID3D11DeviceContext* c,uint64_t vertex,uint64_t pixel,const MeterWindow& region,const std::function<void()>& draw) {
    if(!region.valid || vertex!=0x6cf3fcd45c306511ull || pixel!=0xc90bdf19e7c580ceull || c->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    UINT count=1;D3D11_VIEWPORT vp{};c->RSGetViewports(&count,&vp);
    if(count!=1 || vp.Width!=16 || vp.Height!=16 || vp.TopLeftX!=0 || vp.TopLeftY!=0)return false;
    ComPtr<ID3D11Buffer> global;c->PSGetConstantBuffers(0,1,&global);if(!global)return false;
    D3D11_BUFFER_DESC bd{};global->GetDesc(&bd);if(bd.ByteWidth<48)return false;
    ComPtr<ID3D11Device> d;c->GetDevice(&d);
    if(device_.Get()!=d.Get()){device_=d;shader_.Reset();constants_.Reset();attempted_=false;}
    if(!attempted_) {
        attempted_=true;
        const char source[]=R"(
cbuffer Globals:register(b0) { float4 g[3]; }
cbuffer Window:register(b13) { float4 region; }
Texture2D<float4> luminance:register(t0);SamplerState originalSampler:register(s0);
float4 main(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target {
 float sum=0;
 [unroll] for(int y=-3;y<=3;y+=2) [unroll] for(int x=-3;x<=3;x+=2) {
  float value=luminance.Sample(originalSampler,region.xy+(uv+float2(x,y)*g[2].xy)*region.zw).a;
  sum+=isfinite(value)?value:.5;
 }
 return (sum/16).xxxx;
})";
        ComPtr<ID3DBlob> code,error;
        HRESULT hr=D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"main","ps_5_0",D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error);
        if(SUCCEEDED(hr))hr=d->CreatePixelShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader_);
        D3D11_BUFFER_DESC desc{};desc.ByteWidth=16;desc.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        if(SUCCEEDED(hr))hr=d->CreateBuffer(&desc,nullptr,&constants_);
        if(FAILED(hr)){shader_.Reset();constants_.Reset();Log("Exposure window unavailable hr=%08x",unsigned(hr));}
        else Log("Exposure window initialized: native alpha reduction");
    }
    if(!shader_ || !constants_)return false;
    ComPtr<ID3D11PixelShader> originalShader;UINT classes=0;c->PSGetShader(&originalShader,nullptr,&classes);if(classes)return false;
    struct Restore {
        ID3D11DeviceContext* c;ComPtr<ID3D11PixelShader> shader;ComPtr<ID3D11Buffer> constants;
        ~Restore(){c->PSSetShader(shader.Get(),nullptr,0);auto b=constants.Get();c->PSSetConstantBuffers(13,1,&b);}
    } restore{c};
    restore.shader=originalShader;
    c->PSGetConstantBuffers(13,1,&restore.constants);
    c->UpdateSubresource(constants_.Get(),0,nullptr,region.bounds.data(),0,0);
    auto cb=constants_.Get();c->PSSetConstantBuffers(13,1,&cb);c->PSSetShader(shader_.Get(),nullptr,0);
    draw();return true;
}
}
