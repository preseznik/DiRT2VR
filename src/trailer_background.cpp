#include "trailer_background.h"
#include "common.h"
#include <cmath>
#include <cstring>
#include <d3dcompiler.h>
using Microsoft::WRL::ComPtr;
namespace vr {
TrailerBackgroundRays TrailerBackgroundTransform(const float* nativeView,const float* nativeProjection,
    const float* eyeView,const float* eyeProjection) {
    if(!nativeView || !nativeProjection || !eyeView || !eyeProjection)return {};
    for(const auto p:{nativeView,nativeProjection,eyeView,eyeProjection})
        for(unsigned i=0;i<16;++i)if(!std::isfinite(p[i]))return {};
    for(const auto p:{nativeProjection,eyeProjection})
        if(p[0]<=0 || p[5]<=0 || p[11]!=-1 || p[15]!=0)return {};
    for(const auto v:{nativeView,eyeView})
        for(unsigned a=0;a<3;++a)for(unsigned b=0;b<3;++b) {
            float dot=0;for(unsigned k=0;k<3;++k)dot+=v[4*k+a]*v[4*k+b];
            if(std::abs(dot-(a==b?1.f:0.f))>.001f)return {};
        }
    TrailerBackgroundRays result;
    const float rays[3][3]={{1/eyeProjection[0],0,0},{0,1/eyeProjection[5],0},
        {eyeProjection[8]/eyeProjection[0],eyeProjection[9]/eyeProjection[5],-1}};
    for(unsigned ray=0;ray<3;++ray) {
        float world[3]{},native[3]{};
        // Row vectors: inverse eye rotation is its transpose. This distant
        // cached background has no depth, so translation is intentionally absent.
        for(unsigned c=0;c<3;++c)for(unsigned k=0;k<3;++k)world[c]+=rays[ray][k]*eyeView[c*4+k];
        for(unsigned c=0;c<3;++c)for(unsigned k=0;k<3;++k)native[c]+=world[k]*nativeView[k*4+c];
        for(unsigned c=0;c<4;++c)for(unsigned k=0;k<3;++k)result.values[ray*4+c]+=native[k]*nativeProjection[k*4+c];
    }
    result.valid=true;return result;
}
bool TrailerBackgroundCoversView(const TrailerBackgroundRays& rays) {
    if(!rays.valid)return false;
    for(float x:{-1.f,1.f})for(float y:{-1.f,1.f}) {
        float p[4]{};for(unsigned c=0;c<4;++c)p[c]=x*rays.values[c]+y*rays.values[4+c]+rays.values[8+c];
        if(!std::isfinite(p[3]) || p[3]<=.0001f)return false;
        for(unsigned c=0;c<2;++c)if(!std::isfinite(p[c]) || std::abs(p[c])>p[3]+.00001f)return false;
    }
    return true;
}
bool TrailerBackground::Draw(ID3D11DeviceContext* c,uint64_t vertex,uint64_t pixel,const TrailerBackgroundRays& rays,const std::function<void()>& draw) {
    if(!rays.valid || vertex!=0x3915396cb08d8077ull || pixel!=0x1951f080e2694355ull ||
       c->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    ComPtr<ID3D11Buffer> globals;c->PSGetConstantBuffers(0,1,&globals);if(!globals)return false;
    D3D11_BUFFER_DESC gd{};globals->GetDesc(&gd);if(gd.ByteWidth!=32)return false;
    ComPtr<ID3D11Device> device;c->GetDevice(&device);
    if(device_.Get()!=device.Get()){device_=device;shader_.Reset();constants_.Reset();attempted_=false;}
    if(!attempted_) {
        attempted_=true;
        const char source[]=R"(
cbuffer Globals:register(b0) { float4 g[2]; }
cbuffer Rays:register(b13) { float4 rayX,rayY,rayZ; }
Texture2D<float4> sourceImage:register(t0);SamplerState originalSampler:register(s0);
float4 main(float4 position:SV_Position,float2 uv:TEXCOORD0):SV_Target {
 float4 p=(uv.x*2-1)*rayX+(1-uv.y*2)*rayY+rayZ;
 if(p.w<=.0001)return float4(0,0,0,1);
 float2 sourceUv=p.xy/p.w*float2(.5,-.5)+.5;
 return float4(max(sourceImage.Sample(originalSampler,sourceUv).rgb*g[1].rgb,0),1);
})";
        ComPtr<ID3DBlob> code,error;
        auto hr=D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"main","ps_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error);
        if(SUCCEEDED(hr))hr=device->CreatePixelShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader_);
        D3D11_BUFFER_DESC desc{};desc.ByteWidth=48;desc.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        if(SUCCEEDED(hr))hr=device->CreateBuffer(&desc,nullptr,&constants_);
        if(FAILED(hr)){shader_.Reset();constants_.Reset();Log("Trailer background unavailable hr=%08x",unsigned(hr));}
    }
    if(!shader_ || !constants_)return false;
    ComPtr<ID3D11PixelShader> original;UINT classes=0;c->PSGetShader(&original,nullptr,&classes);if(classes)return false;
    struct Restore {
        ID3D11DeviceContext* context;ComPtr<ID3D11PixelShader> shader;ComPtr<ID3D11Buffer> constants;
        ~Restore(){context->PSSetShader(shader.Get(),nullptr,0);auto cb=constants.Get();context->PSSetConstantBuffers(13,1,&cb);}
    } restore{c,original};
    c->PSGetConstantBuffers(13,1,&restore.constants);
    c->UpdateSubresource(constants_.Get(),0,nullptr,rays.values.data(),0,0);
    auto cb=constants_.Get();c->PSSetConstantBuffers(13,1,&cb);c->PSSetShader(shader_.Get(),nullptr,0);
    draw();return true;
}
}
