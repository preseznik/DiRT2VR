#include "photo_filter.h"
#include "common.h"
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <mutex>
#include <cwchar>
#include <cwctype>

namespace vr {
namespace {
using Microsoft::WRL::ComPtr;
// The frame holds display (gamma) values: colour gains and brightness work in
// approximately linear light, the rest on the display values like a photo editor.
constexpr char source[]=R"(
cbuffer Filter : register(b0) {
    float4 tintExposure;                       // rgb gains, w = 2^exposure
    float saturation,contrast,splitTone,sepia;
    float fade,vignette,grain,seed;
    float bar,aspect,grid,mode;                // bar = letterbox height fraction; mode 1 = grid overlay
    float2 texel; float2 pad;
    float4 userTint;                           // Player filter: tint colour, w = amount
    float offset,gammaPower,pad2,pad3;         // Player filter: brightness offset, display gamma
}
Texture2D colour : register(t0);
SamplerState pointClamp : register(s0);
struct V { float4 position:SV_Position; float2 uv:TEXCOORD0; };
V vs(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1); return v; }
float Line(float coordinate,float width) { return 1-saturate(abs(coordinate)/width); }
float4 ps(V v):SV_Target {
    float3 c=colour.SampleLevel(pointClamp,v.uv,0).rgb;
    if(mode>0.5) {
        // Thirds of the visible picture, one pixel wide, half white.
        if(v.uv.y<bar || v.uv.y>1-bar) return float4(c,1);
        float2 p=float2(v.uv.x,(v.uv.y-bar)/(1-2*bar));
        float2 w=texel*float2(1,1/(1-2*bar));
        float g=max(max(Line(p.x-1.0/3,w.x),Line(p.x-2.0/3,w.x)),max(Line(p.y-1.0/3,w.y),Line(p.y-2.0/3,w.y)));
        return float4(lerp(c,1,0.5*g),1);
    }
    c=pow(saturate(pow(max(c,0),2.2)*tintExposure.rgb*tintExposure.w),1/2.2);
    float luma=dot(c,float3(0.299,0.587,0.114));
    c=lerp(luma.xxx,c,saturation);
    c=(c-0.5)*contrast+0.5;
    c=lerp(c,c*userTint.rgb,userTint.w);
    c=pow(saturate(c+offset),gammaPower);
    // Cinematic split toning: teal shadows, warm highlights.
    c+=splitTone*lerp(float3(-0.04,0.02,0.05),float3(0.06,0.02,-0.05),smoothstep(0.2,0.8,luma));
    float3 toned=float3(dot(c,float3(0.393,0.769,0.189)),dot(c,float3(0.349,0.686,0.168)),dot(c,float3(0.272,0.534,0.131)));
    c=lerp(c,toned,sepia);
    c=lerp(c,0.1+0.82*c,fade);                 // Lifted blacks, softened whites.
    float2 d=(v.uv-0.5)*float2(aspect,1);
    float r=length(d)/length(float2(aspect,1)*0.5);
    c*=1-vignette*smoothstep(0.4,1.0,r);
    float n=frac(sin(dot(floor(v.uv/texel)+seed,float2(12.9898,78.233)))*43758.5453)-0.5;
    c+=grain*0.12*n;
    if(v.uv.y<bar || v.uv.y>1-bar) c=0;
    return float4(saturate(c),1);
}
)";
struct Constants {
    float tintExposure[4];
    float saturation,contrast,splitTone,sepia;
    float fade,vignette,grain,seed;
    float bar,aspect,grid,mode;
    float texel[2],pad[2];
    float userTint[4];
    float offset,gammaPower,pad2,pad3;
};
std::mutex mutex;
struct Pass {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext1> context;
    ComPtr<ID3DDeviceContextState> state;
    ComPtr<ID3D11VertexShader> vs;
    ComPtr<ID3D11PixelShader> ps;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> raster;
    ComPtr<ID3D11DepthStencilState> depth;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11Texture2D> colour;
    ComPtr<ID3D11ShaderResourceView> colourView;
    D3D11_TEXTURE2D_DESC colourDesc{};
    bool ready{},attempted{};
} pass;

bool Prepare(ID3D11Device* device) {
    if(pass.device.Get()!=device){pass=Pass{};pass.device=device;}
    if(pass.attempted)return pass.ready;
    pass.attempted=true;
    ComPtr<ID3D11Device1> device1;ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&device1))) || FAILED(context.As(&pass.context)))return false;
    const D3D_FEATURE_LEVEL level=device->GetFeatureLevel();
    if(FAILED(device1->CreateDeviceContextState(0,&level,1,D3D11_SDK_VERSION,__uuidof(ID3D11Device),nullptr,&pass.state)))return false;
    ComPtr<ID3DBlob> vertex,pixel,error;
    const auto compile=[&](const char* entry,const char* target,ComPtr<ID3DBlob>& out) {
        const HRESULT hr=D3DCompile(source,std::strlen(source),nullptr,nullptr,nullptr,entry,target,D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&out,&error);
        if(FAILED(hr))Log("photo filter: %s compile failed: %s",entry,error?static_cast<const char*>(error->GetBufferPointer()):"");
        return SUCCEEDED(hr);
    };
    if(!compile("vs","vs_5_0",vertex) || !compile("ps","ps_5_0",pixel))return false;
    D3D11_SAMPLER_DESC sampler{};sampler.Filter=D3D11_FILTER_MIN_MAG_MIP_POINT;
    sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sampler.MaxLOD=D3D11_FLOAT32_MAX;
    D3D11_RASTERIZER_DESC raster{};raster.FillMode=D3D11_FILL_SOLID;raster.CullMode=D3D11_CULL_NONE;raster.DepthClipEnable=TRUE;
    D3D11_DEPTH_STENCIL_DESC depth{};depth.DepthFunc=D3D11_COMPARISON_ALWAYS;
    D3D11_BUFFER_DESC constants{};constants.ByteWidth=sizeof(Constants);constants.Usage=D3D11_USAGE_DEFAULT;constants.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    if(FAILED(device->CreateVertexShader(vertex->GetBufferPointer(),vertex->GetBufferSize(),nullptr,&pass.vs)) ||
       FAILED(device->CreatePixelShader(pixel->GetBufferPointer(),pixel->GetBufferSize(),nullptr,&pass.ps)) ||
       FAILED(device->CreateSamplerState(&sampler,&pass.sampler)) || FAILED(device->CreateRasterizerState(&raster,&pass.raster)) ||
       FAILED(device->CreateDepthStencilState(&depth,&pass.depth)) || FAILED(device->CreateBuffer(&constants,nullptr,&pass.constants)))return false;
    Log("photo filter: pass ready");
    return pass.ready=true;
}
// Copies the frame into the pass's source texture and redraws it through the shader.
bool Draw(IDXGISwapChain* swapchain,Constants c) {
    ComPtr<ID3D11Texture2D> back;
    if(!swapchain || FAILED(swapchain->GetBuffer(0,IID_PPV_ARGS(&back))))return false;
    ComPtr<ID3D11Device> device;back->GetDevice(&device);
    if(!Prepare(device.Get()))return false;
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
    c.texel[0]=1.f/float(desc.Width);c.texel[1]=1.f/float(desc.Height);c.aspect=float(desc.Width)/float(desc.Height);
    ComPtr<ID3DDeviceContextState> previous;
    context->SwapDeviceContextState(pass.state.Get(),&previous);
    context->UpdateSubresource(pass.constants.Get(),0,nullptr,&c,0,0);
    auto* buffer=pass.constants.Get();auto* view=pass.colourView.Get();auto* sampler=pass.sampler.Get();auto* rtv=target.Get();
    D3D11_VIEWPORT full{0,0,float(desc.Width),float(desc.Height),0,1};
    context->IASetInputLayout(nullptr);context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->VSSetShader(pass.vs.Get(),nullptr,0);context->PSSetShader(pass.ps.Get(),nullptr,0);
    context->RSSetState(pass.raster.Get());context->RSSetViewports(1,&full);
    context->OMSetDepthStencilState(pass.depth.Get(),0);context->OMSetBlendState(nullptr,nullptr,0xffffffff);
    context->PSSetConstantBuffers(0,1,&buffer);context->PSSetShaderResources(0,1,&view);context->PSSetSamplers(0,1,&sampler);
    context->OMSetRenderTargets(1,&rtv,nullptr);
    context->Draw(3,0);
    ID3D11ShaderResourceView* none{};context->PSSetShaderResources(0,1,&none);
    context->OMSetRenderTargets(0,nullptr,nullptr);
    context->SwapDeviceContextState(previous.Get(),nullptr);
    return true;
}
float Bar(IDXGISwapChain* swapchain,int letterbox) {
    DXGI_SWAP_CHAIN_DESC desc{};
    if(!swapchain || FAILED(swapchain->GetDesc(&desc)) || !desc.BufferDesc.Height)return 0;
    return PhotoLetterboxBar(float(desc.BufferDesc.Width)/float(desc.BufferDesc.Height),
        PhotoLetterboxes[std::clamp(letterbox,0,int(PhotoLetterboxes.size())-1)]);
}
}

const std::vector<PhotoUserLook>& PhotoUserLooks() {
    static const std::vector<PhotoUserLook> looks=[] {
        std::vector<PhotoUserLook> result;
        wchar_t path[MAX_PATH]{};
        const auto length=GetEnvironmentVariableW(L"DIRT2VR_PHOTO_LOOKS",path,MAX_PATH);
        if(!length || length>=MAX_PATH)return result;
        const HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE){Log("photo filter: player looks unreadable");return result;}
        std::string bytes(64*1024,'\0');DWORD read{};
        const bool ok=ReadFile(file,bytes.data(),DWORD(bytes.size()),&read,nullptr);CloseHandle(file);
        if(!ok)return result;
        bytes.resize(read);
        std::wstring text(size_t(MultiByteToWideChar(CP_UTF8,0,bytes.data(),int(bytes.size()),nullptr,0)),L'\0');
        MultiByteToWideChar(CP_UTF8,0,bytes.data(),int(bytes.size()),text.data(),int(text.size()));
        const auto same=[](const std::wstring& a,const wchar_t* b) {
            return a.size()==std::wcslen(b) && std::equal(a.begin(),a.end(),b,[](wchar_t x,wchar_t y){return std::towlower(x)==std::towlower(y);});
        };
        size_t start=0;
        while(start<text.size() && result.size()<64) {
            auto end=text.find(L'\n',start);if(end==std::wstring::npos)end=text.size();
            std::vector<std::wstring> fields;size_t at=start;
            while(at<=end){auto tab=text.find(L'\t',at);if(tab==std::wstring::npos || tab>end)tab=end;fields.push_back(text.substr(at,tab-at));at=tab+1;}
            start=end+1;
            if(fields.size()!=7 || fields[0].empty() || fields[0].size()>64 || fields[5].size()!=6)continue;
            const auto number=[](const std::wstring& s,int low,int high,int& out){wchar_t* e{};const long v=std::wcstol(s.c_str(),&e,10);out=int(v);return e && !*e && !s.empty() && v>=low && v<=high;};
            int brightness{},contrast{},saturation{},gamma{},strength{};
            wchar_t* hexEnd{};const unsigned long rgb=std::wcstoul(fields[5].c_str(),&hexEnd,16);
            if(!number(fields[1],-100,100,brightness) || !number(fields[2],0,200,contrast) || !number(fields[3],0,200,saturation) ||
               !number(fields[4],50,200,gamma) || !number(fields[6],0,100,strength) || !hexEnd || *hexEnd)continue;
            PhotoUserLook look;look.name=fields[0];
            for(const auto& builtIn:PhotoLooks)if(same(look.name,builtIn.name)){look.name+=L" (DiRT2VR)";break;}
            look.brightness=brightness/1000.f;look.contrast=contrast/100.f;look.saturation=saturation/100.f;look.gamma=gamma/100.f;
            look.tint={((rgb>>16)&255)/255.f,((rgb>>8)&255)/255.f,(rgb&255)/255.f};look.tintAmount=strength/100.f;
            result.push_back(std::move(look));
        }
        Log("photo filter: player looks=%u",unsigned(result.size()));
        return result;
    }();
    return looks;
}
int PhotoLookCount(){return int(PhotoLooks.size()+PhotoUserLooks().size());}
std::wstring PhotoLookName(int look) {
    look=std::clamp(look,0,PhotoLookCount()-1);
    return look<int(PhotoLooks.size())?std::wstring(PhotoLooks[look].name):PhotoUserLooks()[look-PhotoLooks.size()].name;
}

bool PhotoFilterApply(IDXGISwapChain* swapchain,const PhotoFilterSettings& settings,unsigned frame) {
    const int index=std::clamp(settings.look,0,PhotoLookCount()-1);
    const float exposure=std::clamp(settings.exposure,-2.f,2.f);
    const float bar=Bar(swapchain,settings.letterbox);
    if(index==0 && std::abs(exposure)<.01f && bar==0)return true; // Nothing to change.
    Constants c{};
    c.tintExposure[0]=c.tintExposure[1]=c.tintExposure[2]=1;c.tintExposure[3]=std::exp2(exposure);
    c.saturation=c.contrast=c.gammaPower=1;
    if(index<int(PhotoLooks.size())) {
        const auto& look=PhotoLooks[index];
        c.tintExposure[0]=look.tint[0];c.tintExposure[1]=look.tint[1];c.tintExposure[2]=look.tint[2];
        c.saturation=look.saturation;c.contrast=look.contrast;c.splitTone=look.splitTone;c.sepia=look.sepia;
        c.fade=look.fade;c.vignette=look.vignette;c.grain=look.grain;
    } else {
        // The player's filter, applied on top of the picture as the game drew it.
        const auto& look=PhotoUserLooks()[index-PhotoLooks.size()];
        c.saturation=look.saturation;c.contrast=look.contrast;c.offset=look.brightness;c.gammaPower=1/look.gamma;
        c.userTint[0]=look.tint[0];c.userTint[1]=look.tint[1];c.userTint[2]=look.tint[2];c.userTint[3]=look.tintAmount;
    }
    c.seed=float(frame%997);
    c.bar=bar;
    std::lock_guard lock(mutex);
    return Draw(swapchain,c);
}
bool PhotoGridDraw(IDXGISwapChain* swapchain,const PhotoFilterSettings& settings) {
    if(!settings.grid)return true;
    Constants c{};c.bar=Bar(swapchain,settings.letterbox);c.grid=1;c.mode=1;
    std::lock_guard lock(mutex);
    return Draw(swapchain,c);
}
}
