#include "game_xr.h"
#include "common.h"
#include <dxgi.h>
#include <cstring>
using Microsoft::WRL::ComPtr;

namespace {
bool Check(XrResult result,const char* operation) {
    vr::Log("OpenXR %s=%d",operation,result); return XR_SUCCEEDED(result);
}
}
bool GameXr::Initialize(ID3D11Device* device,float scale) {
    if(instance_ || !device) return false;
    const char* extensions[]={XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
    XrInstanceCreateInfo create{XR_TYPE_INSTANCE_CREATE_INFO};
    strcpy_s(create.applicationInfo.applicationName,"DiRT2VR cockpit diagnostic");
    create.applicationInfo.apiVersion=XR_MAKE_VERSION(1,0,0);
    create.enabledExtensionCount=1; create.enabledExtensionNames=extensions;
    if(!Check(xrCreateInstance(&create,&instance_),"create instance")) return false;
    XrSystemGetInfo get{XR_TYPE_SYSTEM_GET_INFO}; get.formFactor=XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    XrSystemId system{};
    if(!Check(xrGetSystem(instance_,&get,&system),"get system")) { Shutdown(); return false; }
    PFN_xrGetD3D11GraphicsRequirementsKHR requirementsFn{};
    if(!Check(xrGetInstanceProcAddr(instance_,"xrGetD3D11GraphicsRequirementsKHR",reinterpret_cast<PFN_xrVoidFunction*>(&requirementsFn)),"get requirements function")) { Shutdown(); return false; }
    XrGraphicsRequirementsD3D11KHR requirements{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
    if(!Check(requirementsFn(instance_,system,&requirements),"graphics requirements")) { Shutdown(); return false; }
    ComPtr<IDXGIDevice> dxgi; ComPtr<IDXGIAdapter> adapter; DXGI_ADAPTER_DESC desc{};
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgi))) || FAILED(dxgi->GetAdapter(&adapter)) ||
       FAILED(adapter->GetDesc(&desc)) || std::memcmp(&desc.AdapterLuid,&requirements.adapterLuid,sizeof(LUID)) ||
       device->GetFeatureLevel()<requirements.minFeatureLevel) {
        vr::Log("OpenXR incompatible game graphics adapter or feature level"); Shutdown(); return false;
    }
    XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR}; binding.device=device;
    XrSessionCreateInfo session{XR_TYPE_SESSION_CREATE_INFO}; session.next=&binding; session.systemId=system;
    if(!Check(xrCreateSession(instance_,&session,&session_),"create game session")) { Shutdown(); return false; }
    frames_=std::make_unique<XrFrames>([](const char* message) { vr::Log("OpenXR %s",message); });
    if(!frames_->Initialize(instance_,system,session_,device,scale) || !blit_.Initialize(device)) { Shutdown(); return false; }
    vr::Log("OpenXR game session initialized; experimental cameras, visibility unvalidated"); return true;
}
bool GameXr::Tick(const XrFrames::Draw& draw,const XrFrames::Prepare& prepare) { return frames_ && frames_->Tick(draw,prepare); }
bool GameXr::CopyEye(unsigned eye,ID3D11Texture2D* image,ID3D11RenderTargetView* target,unsigned w,unsigned h) {
    return blit_.Draw(eye,image,target,w,h);
}
void GameXr::Shutdown() {
    frames_.reset();
    if(session_) { xrDestroySession(session_); session_=XR_NULL_HANDLE; }
    if(instance_) { xrDestroyInstance(instance_); instance_=XR_NULL_HANDLE; }
}
