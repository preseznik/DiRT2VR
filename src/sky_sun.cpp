#include "sky_sun.h"
#include "common.h"
#include <d3dcompiler.h>
#include <cstring>
using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
// Native sky PS a0291237968fd075, with only the sun direction changed:
// skyParams3.xyz is adjusted for the driving camera; skyParams4.xyz retains
// the world sunlight direction. Keep the main pass's sharpness, not the
// reflection variant's skyParams3.w, and preserve its sky/haze colour math.
constexpr char source[]=R"(
cbuffer Globals:register(b0) { float4 skyParams,skyParams2,skyParams3,skyParams4; }
cbuffer PerFrame:register(b1) { float4 frame[17]; }
Texture2D<float4> skyTexture:register(t0);SamplerState skySampler:register(s0);
float4 main(float4 position:SV_Position,centroid float2 uv:TEXCOORD0,centroid float3 ray:TEXCOORD1):SV_Target {
    float alignment=saturate(dot(normalize(ray),skyParams4.xyz));
    float sun=exp2(log2(alignment)*skyParams.z);
    float haze=exp2(log2(alignment)*frame[11].w);
    float3 sky=exp2(log2(skyTexture.Sample(skySampler,uv).rgb*skyParams.x)*skyParams.y);
    float3 colour=mad(haze,frame[11].xyz-sky,sky);
    return float4(max(mad(sun,skyParams2.xyz,colour),0),1);
}
)";
// Native sky VS d101288c9b956209, retaining clip position and texture UVs.
// Interpolate the full viewing rays; normalizing at vertices makes the sun
// drift as their distances from the camera change. The PS normalizes per pixel.
constexpr char vertexSource[]=R"(
cbuffer Geometry:register(b0) { float4 geometry[24]; }
cbuffer Camera:register(b3) { float4 camera[25]; }
struct Output { float4 position:SV_Position;float2 uv:TEXCOORD0;float3 ray:TEXCOORD1; };
Output main(float3 position:POSITION,float2 uv:TEXCOORD0) {
    float3 clip=position.y*geometry[5].xyw;
    clip=mad(position.x,geometry[4].xyw,clip);
    clip=mad(position.z,geometry[6].xyw,clip)+geometry[7].xyw;
    float3 eye=position.y*geometry[9].xyz;
    eye=mad(position.x,geometry[8].xyz,eye);
    eye=mad(position.z,geometry[10].xyz,eye)+geometry[11].xyz;
    Output output;
    output.position=float4(clip.xy,clip.z,clip.z);
    output.uv=uv;
    output.ray=float3(dot(camera[0].xyz,eye),dot(camera[1].xyz,eye),dot(camera[2].xyz,eye));
    return output;
}
)";
}
bool SkySunPass::Draw(ID3D11DeviceContext* context,uint64_t vertex,uint64_t pixel,const std::function<void()>& draw) {
    if(vertex!=0xd101288c9b956209ull || pixel!=0xa0291237968fd075ull ||
       context->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)return false;
    ComPtr<ID3D11Buffer> globals,frame,geometry,camera;
    context->PSGetConstantBuffers(0,1,&globals);context->PSGetConstantBuffers(1,1,&frame);
    context->VSGetConstantBuffers(0,1,&geometry);context->VSGetConstantBuffers(3,1,&camera);
    if(!globals || !frame || !geometry || !camera)return false;
    D3D11_BUFFER_DESC gd{},fd{},vd{},cd{};globals->GetDesc(&gd);frame->GetDesc(&fd);
    geometry->GetDesc(&vd);camera->GetDesc(&cd);
    if(gd.ByteWidth!=64 || fd.ByteWidth!=272 || vd.ByteWidth!=384 || cd.ByteWidth!=400)return false;
    ComPtr<ID3D11PixelShader> original;ComPtr<ID3D11VertexShader> originalVertex;UINT classes=0,vertexClasses=0;
    context->PSGetShader(&original,nullptr,&classes);
    context->VSGetShader(&originalVertex,nullptr,&vertexClasses);
    if(!original || !originalVertex || classes || vertexClasses)return false;
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(device_.Get()!=device.Get()){device_=device;shader_.Reset();vertex_.Reset();attempted_=false;}
    if(!attempted_) {
        attempted_=true;
        ComPtr<ID3DBlob> code,error;
        auto hr=D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,"main","ps_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error);
        if(SUCCEEDED(hr))hr=device->CreatePixelShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader_);
        code.Reset();error.Reset();
        if(SUCCEEDED(hr))hr=D3DCompile(vertexSource,std::strlen(vertexSource),nullptr,nullptr,nullptr,"main","vs_5_0",
            D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&code,&error);
        if(SUCCEEDED(hr))hr=device->CreateVertexShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&vertex_);
        if(FAILED(hr)){shader_.Reset();vertex_.Reset();Log("VR sun direction and ray correction unavailable hr=%08x",unsigned(hr));}
        else Log("VR sun direction and ray correction initialized");
    }
    if(!shader_ || !vertex_)return false;
    struct Restore {
        ID3D11DeviceContext* context;
        ID3D11PixelShader* shader;
        ID3D11VertexShader* vertex;
        ~Restore(){context->VSSetShader(vertex,nullptr,0);context->PSSetShader(shader,nullptr,0);}
    } restore{context,original.Get(),originalVertex.Get()};
    context->VSSetShader(vertex_.Get(),nullptr,0);
    context->PSSetShader(shader_.Get(),nullptr,0);
    draw();return true;
}
}
