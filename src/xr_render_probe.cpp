#include "xr_frames.h"
#include <d3dcompiler.h>
#include <xr_linear.h>
#include <cstdio>
#include <stdexcept>
using Microsoft::WRL::ComPtr;

bool RenderProbe(XrInstance instance,XrSystemId system,XrSession session,ID3D11Device* device) {
    XrFrames frames;
    if(!frames.Initialize(instance,system,session,device,0.5f)) { puts("Frame resource initialization failed"); return false; }
    const char* source=R"(
cbuffer Camera : register(b0) { float4x4 mvp; }
struct V {float4 p:SV_Position; float3 c:COLOR;};
V vs(uint id:SV_VertexID) {
  static const float3 p[8]={float3(-1,-1,-1),float3(1,-1,-1),float3(1,1,-1),float3(-1,1,-1),float3(-1,-1,1),float3(1,-1,1),float3(1,1,1),float3(-1,1,1)};
  static const uint indices[36]={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,2,3,7,2,7,6,1,2,6,1,6,5,3,0,4,3,4,7};
  V v; float3 local=p[indices[id]];
  v.p=mul(mvp,float4(local*0.25+float3(0,0,-1.5),1));
  v.c=local*0.35+0.5;return v;
}
float4 ps(V v):SV_Target {return float4(v.c,1);}
)";
    ComPtr<ID3DBlob> vsBytes,psBytes,error;
    if(FAILED(D3DCompile(source,strlen(source),nullptr,nullptr,nullptr,"vs","vs_5_0",0,0,&vsBytes,&error))) { printf("VS compile: %s\n",error?static_cast<char*>(error->GetBufferPointer()):"unknown"); return false; }
    if(FAILED(D3DCompile(source,strlen(source),nullptr,nullptr,nullptr,"ps","ps_5_0",0,0,&psBytes,&error))) { printf("PS compile: %s\n",error?static_cast<char*>(error->GetBufferPointer()):"unknown"); return false; }
    ComPtr<ID3D11VertexShader> vs; ComPtr<ID3D11PixelShader> ps;
    if(FAILED(device->CreateVertexShader(vsBytes->GetBufferPointer(),vsBytes->GetBufferSize(),nullptr,&vs)) ||
       FAILED(device->CreatePixelShader(psBytes->GetBufferPointer(),psBytes->GetBufferSize(),nullptr,&ps))) return false;
    D3D11_BUFFER_DESC bd{}; bd.ByteWidth=64; bd.Usage=D3D11_USAGE_DEFAULT; bd.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
    ComPtr<ID3D11Buffer> constants;
    if(FAILED(device->CreateBuffer(&bd,nullptr,&constants))) return false;
    D3D11_RASTERIZER_DESC rd{}; rd.FillMode=D3D11_FILL_SOLID; rd.CullMode=D3D11_CULL_NONE; rd.DepthClipEnable=TRUE;
    ComPtr<ID3D11RasterizerState> raster;
    if(FAILED(device->CreateRasterizerState(&rd,&raster))) return false;
    ComPtr<ID3D11DeviceContext> context; device->GetImmediateContext(&context);
    std::array<ComPtr<ID3D11DepthStencilView>,2> depth;
    const auto start=GetTickCount64();
    ULONGLONG visibleStart{};
    printf("Rendering a stationary diagnostic cube for 20 visible seconds (60s startup limit); this is NOT DiRT 2.\n");
    while(GetTickCount64()-start<60000 && (!visibleStart || GetTickCount64()-visibleStart<20000) && !frames.Exiting()) {
        bool drew=frames.Tick([&](unsigned eye,const XrView& view,ID3D11RenderTargetView* target,uint32_t w,uint32_t h) {
            if(!depth[eye]) {
                D3D11_TEXTURE2D_DESC td{}; td.Width=w; td.Height=h; td.MipLevels=1; td.ArraySize=1;
                td.Format=DXGI_FORMAT_D32_FLOAT; td.SampleDesc.Count=1; td.BindFlags=D3D11_BIND_DEPTH_STENCIL;
                ComPtr<ID3D11Texture2D> texture;
                if(FAILED(device->CreateTexture2D(&td,nullptr,&texture)) || FAILED(device->CreateDepthStencilView(texture.Get(),nullptr,&depth[eye]))) throw std::runtime_error("depth creation");
            }
            XrMatrix4x4f camera,viewMatrix,projection,mvp;
            XrMatrix4x4f_CreateFromRigidTransform(&camera,&view.pose);
            XrMatrix4x4f_InvertRigidBody(&viewMatrix,&camera);
            XrMatrix4x4f_CreateProjectionFov(&projection,GRAPHICS_D3D,view.fov,0.05f,100.f);
            XrMatrix4x4f_Multiply(&mvp,&projection,&viewMatrix);
            context->UpdateSubresource(constants.Get(),0,nullptr,&mvp,0,0);
            const float background[]={0.025f,0.035f,0.055f,1.f};
            context->ClearRenderTargetView(target,background);
            context->ClearDepthStencilView(depth[eye].Get(),D3D11_CLEAR_DEPTH,1,0);
            context->OMSetRenderTargets(1,&target,depth[eye].Get());
            D3D11_VIEWPORT viewport{0,0,static_cast<float>(w),static_cast<float>(h),0,1};
            context->RSSetViewports(1,&viewport); context->RSSetState(raster.Get());
            context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            context->VSSetShader(vs.Get(),nullptr,0); context->PSSetShader(ps.Get(),nullptr,0);
            ID3D11Buffer* cb=constants.Get(); context->VSSetConstantBuffers(0,1,&cb);
            context->Draw(36,0);
            context->OMSetRenderTargets(0,nullptr,nullptr);
            context->Flush();
        });
        if(frames.Visible() && !visibleStart) visibleStart=GetTickCount64();
        if(!drew) Sleep(5);
    }
    printf("submitted_stereo_frames=%llu\n",static_cast<unsigned long long>(frames.Submitted()));
    return visibleStart && frames.Submitted()>=60;
}
