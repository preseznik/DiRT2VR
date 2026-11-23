#include "photo_dof.h"
#include "live_neural.h"
#include "distance_blur.h"
#include "photo_mode.h"
#include "common.h"
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <MinHook.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <mutex>
#include <set>

namespace vr {
namespace {
using Microsoft::WRL::ComPtr;
// The game's shadow-mask pass reads the scene depth (t0, screenDepthMap) and its
// projection terms (cb0 offset 192, screenProj). Copy both for the Photo Mode blur.
constexpr char source[]=R"(
cbuffer Game : register(b0) { float4 gameRows[12]; float4 screenProj; }
cbuffer Dof : register(b1) { float focus; float maxRadius; float autoFocus; float radiusStep; float2 texel; float2 depthSize; float2 farRange; float2 unused; }
Texture2D colour : register(t0);
#ifdef MULTISAMPLE
Texture2DMS<float> depthMap : register(t1);
#else
Texture2D<float> depthMap : register(t1);
#endif
SamplerState linearClamp : register(s0);
float Distance(float2 uv) {
    int2 p=int2(clamp(uv,0,0.99999)*depthSize);
#ifdef MULTISAMPLE
    float d=depthMap.Load(p,0);
#else
    float d=depthMap.Load(int3(p,0));
#endif
    // GL-style projection stored directly: depth = -z - w/distance (lab: z=-1.00003, w=0.6).
    float den=-screenProj.z-d;
    return d<1 && den>1e-6 ? screenProj.w/den : 1e6;
}
float Circle(float distance,float f) {
    // Distance blur: sharp up to farRange.x, full strength from farRange.y. The sky (cleared depth) stays as it is:
    // it is smooth anyway, and crowds and other late draws seen against it are not in the depth copy.
    if(farRange.x>0) return distance>=1e5 ? 0 : maxRadius*smoothstep(farRange.x,farRange.y,distance);
    return maxRadius*saturate((abs(distance-f)-max(0.075*f,1.5))/f);
}
float Focus() { return clamp(autoFocus>0.5 ? Distance(float2(0.5,0.5)) : focus,0.3,5000); }
struct V { float4 position:SV_Position; float2 uv:TEXCOORD0; };
V vs(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1); return v; }
// Single-pass gather blur on a golden-angle spiral. Farther samples may not
// blur over a sharper nearer pixel; nearer samples may spill over it.
float4 ps(V v):SV_Target {
    float f=Focus();
    float3 sum=colour.SampleLevel(linearClamp,v.uv,0).rgb;
    float centreDistance=Distance(v.uv),centreSize=Circle(centreDistance,f);
    float total=1,radius=radiusStep,angle=0;
    [loop] for(int i=0;i<160 && radius<maxRadius;++i) {
        float2 tc=v.uv+float2(cos(angle),sin(angle))*texel*radius;
        float3 c=colour.SampleLevel(linearClamp,tc,0).rgb;
        float sampleDistance=Distance(tc),sampleSize=Circle(sampleDistance,f);
        if(sampleDistance>centreDistance) sampleSize=clamp(sampleSize,0,centreSize*2);
        float m=smoothstep(radius-0.5,radius+0.5,sampleSize);
        sum+=lerp(sum/total,c,m); total+=1;
        radius+=radiusStep/radius; angle+=2.39996323;
    }
    return float4(sum/total,1);
}
float4 centre(V v):SV_Target {
    // Pixel 0: picture centre. Pixel 1 (diagnostic): lower centre, usually the car.
    float2 uv=v.position.x<1 ? float2(0.5,0.5) : float2(0.5,0.8);
    int2 p=int2(uv*depthSize);
#ifdef MULTISAMPLE
    float d=depthMap.Load(p,0);
#else
    float d=depthMap.Load(int3(p,0));
#endif
    return float4(d,screenProj.z,screenProj.w,Distance(uv));
}
)";
struct Constants { float focus,maxRadius,autoFocus,radiusStep; float texel[2],depthSize[2]; float farRange[2],unused[2]; };

std::mutex mutex;
std::atomic<bool> wanted{};
std::atomic<float> centreDistance{-1};
using CreatePsFn=HRESULT(STDMETHODCALLTYPE*)(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11PixelShader**);
using DrawFn=void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT);
using DrawInstancedFn=void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT,UINT);
using DrawIndexedInstancedFn=void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT,INT,UINT);
using CreateVsFn=HRESULT(STDMETHODCALLTYPE*)(ID3D11Device*,const void*,SIZE_T,ID3D11ClassLinkage*,ID3D11VertexShader**);
CreatePsFn realCreatePs{};
CreateVsFn realCreateVs{};
DrawInstancedFn realDrawInstanced{};
DrawIndexedInstancedFn realDrawIndexedInstanced{};
DrawFn realDraw{};
std::set<ID3D11PixelShader*> maskShaders;
// Captured this frame.
ComPtr<ID3D11Texture2D> depthCopy;
ComPtr<ID3D11ShaderResourceView> depthView;
ComPtr<ID3D11Buffer> globalsCopy;
D3D11_TEXTURE2D_DESC depthDesc{};
D3D11_SHADER_RESOURCE_VIEW_DESC viewDesc{};
UINT globalsBytes{};
bool copied{},failureLogged{};
// Blur pass.
struct Pass {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext1> context;
    ComPtr<ID3DDeviceContextState> state;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> blur,centre;
    bool multisample{},ready{},attempted{};
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> raster;
    ComPtr<ID3D11DepthStencilState> depth;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11Texture2D> colour;
    ComPtr<ID3D11ShaderResourceView> colourView;
    D3D11_TEXTURE2D_DESC colourDesc{};
    ComPtr<ID3D11Texture2D> centreTarget;
    ComPtr<ID3D11RenderTargetView> centreView;
    std::array<ComPtr<ID3D11Texture2D>,3> staging;
    unsigned ring{};
} pass;

uint64_t Fnv(const void* bytes,SIZE_T length) {
    uint64_t hash=14695981039346656037ull;
    auto data=static_cast<const unsigned char*>(bytes);
    for(SIZE_T i=0;i<length;++i)hash=(hash^data[i])*1099511628211ull;
    return hash;
}
HRESULT STDMETHODCALLTYPE CreatePs(ID3D11Device* device,const void* bytes,SIZE_T length,ID3D11ClassLinkage* linkage,ID3D11PixelShader** shader) {
    const auto hr=realCreatePs(device,bytes,length,linkage,shader);
    if(SUCCEEDED(hr) && shader && *shader && bytes) {
        const auto hash=Fnv(bytes,length);
        if(hash==0x0024789eedc9b7bcull || hash==0xc3d93558eae69b72ull || hash==0xccdb9d07dcf3406eull) {
            std::lock_guard lock(mutex);maskShaders.insert(*shader);
            Log("photo depth of field: shadow-mask shader %016llx recognised",hash);
        }
    }
    return hr;
}
void Observe(ID3D11DeviceContext* context) {
    ComPtr<ID3D11PixelShader> shader;context->PSGetShader(&shader,nullptr,nullptr);
    if(!shader)return;
    std::lock_guard lock(mutex);
    if(copied || !maskShaders.contains(shader.Get()))return;
    ComPtr<ID3D11ShaderResourceView> srv;context->PSGetShaderResources(0,1,&srv);
    ComPtr<ID3D11Buffer> globals;context->PSGetConstantBuffers(0,1,&globals);
    if(!srv || !globals)return;
    ComPtr<ID3D11Resource> resource;srv->GetResource(&resource);
    ComPtr<ID3D11Texture2D> texture;
    if(FAILED(resource.As(&texture)))return;
    D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);
    D3D11_SHADER_RESOURCE_VIEW_DESC view{};srv->GetDesc(&view);
    D3D11_BUFFER_DESC buffer{};globals->GetDesc(&buffer);
    if((view.ViewDimension!=D3D11_SRV_DIMENSION_TEXTURE2D && view.ViewDimension!=D3D11_SRV_DIMENSION_TEXTURE2DMS) || buffer.ByteWidth<208)return;
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(!depthCopy || std::memcmp(&desc,&depthDesc,sizeof(desc)) || std::memcmp(&view,&viewDesc,sizeof(view))) {
        depthCopy.Reset();depthView.Reset();
        auto copy=desc;copy.Usage=D3D11_USAGE_DEFAULT;copy.CPUAccessFlags=0;copy.MiscFlags=0;
        if(FAILED(device->CreateTexture2D(&copy,nullptr,&depthCopy)) || FAILED(device->CreateShaderResourceView(depthCopy.Get(),&view,&depthView))) {
            depthCopy.Reset();depthView.Reset();
            if(!failureLogged){failureLogged=true;Log("photo depth of field: depth copy unavailable format=%u samples=%u",unsigned(desc.Format),desc.SampleDesc.Count);}
            return;
        }
        depthDesc=desc;viewDesc=view;
        Log("photo depth of field: depth source %ux%u format=%u view_format=%u samples=%u dimension=%u",
            desc.Width,desc.Height,unsigned(desc.Format),unsigned(view.Format),desc.SampleDesc.Count,unsigned(view.ViewDimension));
    }
    if(!globalsCopy || globalsBytes!=buffer.ByteWidth) {
        globalsCopy.Reset();
        D3D11_BUFFER_DESC copy{};copy.ByteWidth=buffer.ByteWidth;copy.Usage=D3D11_USAGE_DEFAULT;copy.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        if(FAILED(device->CreateBuffer(&copy,nullptr,&globalsCopy)))return;
        globalsBytes=buffer.ByteWidth;
    }
    context->CopyResource(depthCopy.Get(),texture.Get());
    context->CopyResource(globalsCopy.Get(),globals.Get());
    copied=true;
}
// Live neural rendering: recognise the HUD vertex shaders and move HUD draws
// (instanced UI quads among them) into their own texture.
HRESULT STDMETHODCALLTYPE CreateVs(ID3D11Device* device,const void* bytes,SIZE_T length,ID3D11ClassLinkage* linkage,ID3D11VertexShader** shader) {
    const auto hr=realCreateVs(device,bytes,length,linkage,shader);
    if(SUCCEEDED(hr) && shader && *shader && bytes)LiveNeuralVertexShader(*shader,Fnv(bytes,length));
    return hr;
}
void STDMETHODCALLTYPE DrawInstanced(ID3D11DeviceContext* context,UINT vertices,UINT instances,UINT start,UINT first) {
    const auto draw=[&]{realDrawInstanced(context,vertices,instances,start,first);};
    if(!LiveNeuralHudDraw(context,draw))draw();
}
void STDMETHODCALLTYPE DrawIndexedInstanced(ID3D11DeviceContext* context,UINT indices,UINT instances,UINT start,INT base,UINT first) {
    const auto draw=[&]{realDrawIndexedInstanced(context,indices,instances,start,base,first);};
    if(!LiveNeuralHudDraw(context,draw))draw();
}
void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* context,UINT count,UINT start) {
    if(count==4 && wanted.load(std::memory_order_relaxed) && context->GetType()==D3D11_DEVICE_CONTEXT_IMMEDIATE)Observe(context);
    const auto draw=[&]{realDraw(context,count,start);};
    if(!LiveNeuralHudDraw(context,draw))draw();
}
bool Hook(void* object,unsigned index,void* replacement,void** original) {
    auto target=(*static_cast<void***>(object))[index];
    auto status=MH_CreateHook(target,replacement,original);
    if(status==MH_OK)status=MH_EnableHook(target);
    Log("photo depth of field: hook slot=%u status=%s",index,MH_StatusToString(status));
    return status==MH_OK;
}
bool Prepare(ID3D11Device* device,bool multisample) {
    if(pass.device.Get()!=device) {pass=Pass{};pass.device=device;}
    if(pass.ready && pass.multisample==multisample)return true;
    if(pass.attempted && pass.multisample==multisample)return false;
    pass.attempted=true;pass.ready=false;pass.multisample=multisample;
    ComPtr<ID3D11Device1> device1;ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&device1))) || FAILED(context.As(&pass.context)))return false;
    const D3D_FEATURE_LEVEL level=device->GetFeatureLevel();
    if(!pass.state && FAILED(device1->CreateDeviceContextState(0,&level,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),nullptr,&pass.state)))return false;
    const D3D_SHADER_MACRO macros[]={{"MULTISAMPLE","1"},{nullptr,nullptr}};
    ComPtr<ID3DBlob> vertex,blur,centre,error;
    const auto compile=[&](const char* entry,const char* target,ComPtr<ID3DBlob>& out) {
        const HRESULT hr=D3DCompile(source,std::strlen(source),nullptr,multisample?macros:nullptr,nullptr,entry,target,D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&out,&error);
        if(FAILED(hr))Log("photo depth of field: %s compile failed: %s",entry,error?static_cast<const char*>(error->GetBufferPointer()):"");
        return SUCCEEDED(hr);
    };
    if(!compile("vs","vs_5_0",vertex) || !compile("ps","ps_5_0",blur) || !compile("centre","ps_5_0",centre))return false;
    if(FAILED(device->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&pass.vs)) ||
       FAILED(device->CreatePixelShader(blur->GetBufferPointer(),blur->GetBufferSize(),nullptr,&pass.blur)) ||
       FAILED(device->CreatePixelShader(centre->GetBufferPointer(),centre->GetBufferSize(),nullptr,&pass.centre)))return false;
    D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampler.MaxLOD=D3D11_FLOAT32_MAX;
    D3D11_RASTERIZER_DESC raster{};raster.FillMode=D3D11_FILL_SOLID;raster.CullMode=D3D11_CULL_NONE;raster.DepthClipEnable=TRUE;
    D3D11_DEPTH_STENCIL_DESC depth{};depth.DepthFunc=D3D11_COMPARISON_ALWAYS;
    D3D11_BUFFER_DESC constants{};constants.ByteWidth=sizeof(Constants);constants.Usage=D3D11_USAGE_DEFAULT;constants.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    D3D11_TEXTURE2D_DESC target{};target.Height=target.MipLevels=target.ArraySize=1;target.Width=2;target.Format=DXGI_FORMAT_R32G32B32A32_FLOAT;
    target.SampleDesc.Count=1;target.Usage=D3D11_USAGE_DEFAULT;target.BindFlags=D3D11_BIND_RENDER_TARGET;
    auto staging=target;staging.Usage=D3D11_USAGE_STAGING;staging.BindFlags=0;staging.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    if(FAILED(device->CreateSamplerState(&sampler,&pass.sampler)) || FAILED(device->CreateRasterizerState(&raster,&pass.raster)) ||
       FAILED(device->CreateDepthStencilState(&depth,&pass.depth)) || FAILED(device->CreateBuffer(&constants,nullptr,&pass.constants)) ||
       FAILED(device->CreateTexture2D(&target,nullptr,&pass.centreTarget)) || FAILED(device->CreateRenderTargetView(pass.centreTarget.Get(),nullptr,&pass.centreView)))return false;
    for(auto& s:pass.staging)if(FAILED(device->CreateTexture2D(&staging,nullptr,&s)))return false;
    Log("photo depth of field: blur pass ready multisample_depth=%d",multisample);
    return pass.ready=true;
}
}

void AttachPhotoDof(ID3D11Device* device,ID3D11DeviceContext* context) {
    static std::once_flag once;
    std::call_once(once,[&] {
        const auto status=MH_Initialize();
        if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED){Log("photo depth of field: MinHook unavailable");return;}
        ComPtr<ID3D11DeviceContext> owned;
        if(!context){device->GetImmediateContext(&owned);context=owned.Get();}
        Hook(device,15,reinterpret_cast<void*>(CreatePs),reinterpret_cast<void**>(&realCreatePs));
        // HUD draws: captured for live neural rendering, hidden in replay Photo Mode.
        if(LiveNeuralRequested() || PhotoModeRequested() || DistanceBlurRequested()) {
            Hook(device,12,reinterpret_cast<void*>(CreateVs),reinterpret_cast<void**>(&realCreateVs));
            Hook(context,20,reinterpret_cast<void*>(DrawIndexedInstanced),reinterpret_cast<void**>(&realDrawIndexedInstanced));
            Hook(context,21,reinterpret_cast<void*>(DrawInstanced),reinterpret_cast<void**>(&realDrawInstanced));
        }
        Hook(context,13,reinterpret_cast<void*>(Draw),reinterpret_cast<void**>(&realDraw));
    });
}
void PhotoDofWanted(bool value) {
    if(wanted.exchange(value)==value || value)return;
    std::lock_guard lock(mutex);
    copied=false;centreDistance=-1; // A later session must not blur with an old depth copy.
}
float PhotoDofCentre(){return centreDistance.load();}

bool PhotoDofApply(IDXGISwapChain* swapchain,const PhotoDofSettings& settings) {
    std::lock_guard lock(mutex);
    const bool fresh=copied;copied=false;
    if(!fresh || !depthView || !globalsCopy || !swapchain)return false;
    ComPtr<ID3D11Texture2D> back;
    if(FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return false;
    ComPtr<ID3D11Device> device;back->GetDevice(&device);
    if(!Prepare(device.Get(),viewDesc.ViewDimension==D3D11_SRV_DIMENSION_TEXTURE2DMS))return false;
    D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);
    auto colour=desc;colour.SampleDesc={1,0};colour.Usage=D3D11_USAGE_DEFAULT;colour.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    colour.CPUAccessFlags=0;colour.MiscFlags=0;colour.MipLevels=1;colour.ArraySize=1;
    if(!pass.colour || std::memcmp(&colour,&pass.colourDesc,sizeof(colour))) {
        pass.colour.Reset();pass.colourView.Reset();
        if(FAILED(device->CreateTexture2D(&colour,nullptr,&pass.colour)) ||
           FAILED(device->CreateShaderResourceView(pass.colour.Get(),nullptr,&pass.colourView))){pass.colour.Reset();return false;}
        pass.colourDesc=colour;
    }
    ComPtr<ID3D11RenderTargetView> target;
    if(FAILED(device->CreateRenderTargetView(back.Get(),nullptr,&target)))return false;
    auto* context=pass.context.Get();
    if(desc.SampleDesc.Count>1)context->ResolveSubresource(pass.colour.Get(),0,back.Get(),0,desc.Format);
    else context->CopySubresourceRegion(pass.colour.Get(),0,0,0,0,back.Get(),0,nullptr);
    const float scale=float(desc.Height)/1080.f;
    Constants c{};
    c.focus=std::clamp(settings.focus,.3f,5000.f);
    c.maxRadius=2.5f*float(std::clamp(settings.blur,1,10))*scale;
    c.autoFocus=settings.autoFocus?1.f:0.f;
    c.radiusStep=std::max(.5f,c.maxRadius*c.maxRadius/300.f); // About 150 taps at most.
    c.texel[0]=1.f/float(desc.Width);c.texel[1]=1.f/float(desc.Height);
    c.depthSize[0]=float(depthDesc.Width);c.depthSize[1]=float(depthDesc.Height);
    c.farRange[0]=std::max(0.f,settings.farStart);c.farRange[1]=std::max(c.farRange[0]+1.f,settings.farFull);
    ComPtr<ID3DDeviceContextState> previous;
    context->SwapDeviceContextState(pass.state.Get(),&previous);
    context->UpdateSubresource(pass.constants.Get(),0,nullptr,&c,0,0);
    ID3D11Buffer* buffers[]={globalsCopy.Get(),pass.constants.Get()};
    ID3D11ShaderResourceView* views[]={pass.colourView.Get(),depthView.Get()};
    auto* sampler=pass.sampler.Get();
    context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(pass.vs.Get(),nullptr,0);
    context->RSSetState(pass.raster.Get());context->OMSetDepthStencilState(pass.depth.Get(),0);context->OMSetBlendState(nullptr,nullptr,0xffffffff);
    context->PSSetConstantBuffers(0,2,buffers);context->PSSetShaderResources(0,2,views);context->PSSetSamplers(0,1,&sampler);
    auto* rtv=target.Get();
    D3D11_VIEWPORT full{0,0,float(desc.Width),float(desc.Height),0,1};
    context->OMSetRenderTargets(1,&rtv,nullptr);context->RSSetViewports(1,&full);
    context->PSSetShader(pass.blur.Get(),nullptr,0);context->Draw(3,0);
    // The distance at the centre, for the panel and for switching to manual focus.
    auto* centre=pass.centreView.Get();
    D3D11_VIEWPORT single{0,0,2,1,0,1};
    context->OMSetRenderTargets(1,&centre,nullptr);context->RSSetViewports(1,&single);
    context->PSSetShader(pass.centre.Get(),nullptr,0);context->Draw(3,0);
    ID3D11ShaderResourceView* none[2]{};context->PSSetShaderResources(0,2,none);
    context->OMSetRenderTargets(0,nullptr,nullptr);
    context->SwapDeviceContextState(previous.Get(),nullptr);
    context->CopyResource(pass.staging[pass.ring%3].Get(),pass.centreTarget.Get());
    // Read the copy made two frames ago without waiting for the GPU.
    D3D11_MAPPED_SUBRESOURCE mapped{};
    auto* oldest=pass.staging[(pass.ring+1)%3].Get();
    if(pass.ring>=2 && SUCCEEDED(context->Map(oldest,0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&mapped))) {
        float value[8]{};std::memcpy(value,mapped.pData,sizeof(value));
        context->Unmap(oldest,0);
        if(std::isfinite(value[3]) && value[3]>0)centreDistance=value[3];
        if(pass.ring%60==0)Log("photo depth of field: centre raw=%.7f distance=%.3f lower raw=%.7f distance=%.3f screenProj.zw=%.7f,%.7f",
            value[0],value[3],value[4],value[7],value[1],value[2]);
    }
    ++pass.ring;
    return true;
}
}
