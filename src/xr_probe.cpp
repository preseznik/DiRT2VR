#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <cstdio>
#include <vector>
#include <cstring>
using Microsoft::WRL::ComPtr;

bool RenderProbe(XrInstance,XrSystemId,XrSession,ID3D11Device*);
int main(int argc,char** argv) {
    uint32_t count{};
    XrResult result=xrEnumerateInstanceExtensionProperties(nullptr,0,&count,nullptr);
    printf("xrEnumerateInstanceExtensionProperties=%d\n",result);
    if(XR_FAILED(result)) return 1;
    std::vector<XrExtensionProperties> extensions(count,{XR_TYPE_EXTENSION_PROPERTIES});
    result=xrEnumerateInstanceExtensionProperties(nullptr,count,&count,extensions.data());
    if(XR_FAILED(result)) return 1;
    bool d3d11=false,refreshRate=false;
    for(auto& e:extensions) {
        if(!strcmp(e.extensionName,XR_KHR_D3D11_ENABLE_EXTENSION_NAME)) d3d11=true;
        if(!strcmp(e.extensionName,XR_FB_DISPLAY_REFRESH_RATE_EXTENSION_NAME)) refreshRate=true;
    }
    if(!d3d11) { puts("Runtime does not expose D3D11"); return 2; }
    const char* enabled[]={XR_KHR_D3D11_ENABLE_EXTENSION_NAME,XR_FB_DISPLAY_REFRESH_RATE_EXTENSION_NAME};
    XrInstanceCreateInfo info{XR_TYPE_INSTANCE_CREATE_INFO};
    strcpy_s(info.applicationInfo.applicationName,"DiRT2VR runtime probe");
    info.applicationInfo.apiVersion=XR_MAKE_VERSION(1,0,0);
    info.enabledExtensionCount=refreshRate ? 2 : 1; info.enabledExtensionNames=enabled;
    XrInstance instance{};
    result=xrCreateInstance(&info,&instance);
    printf("xrCreateInstance=%d\n",result);
    if(XR_FAILED(result)) return 3;
    XrInstanceProperties properties{XR_TYPE_INSTANCE_PROPERTIES};
    xrGetInstanceProperties(instance,&properties);
    printf("runtime=%s version=%llu.%llu.%llu architecture=x86\n",properties.runtimeName,
        static_cast<unsigned long long>(XR_VERSION_MAJOR(properties.runtimeVersion)),
        static_cast<unsigned long long>(XR_VERSION_MINOR(properties.runtimeVersion)),
        static_cast<unsigned long long>(XR_VERSION_PATCH(properties.runtimeVersion)));
    XrSystemGetInfo systemInfo{XR_TYPE_SYSTEM_GET_INFO}; systemInfo.formFactor=XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    XrSystemId system{};
    result=xrGetSystem(instance,&systemInfo,&system);
    printf("xrGetSystem=%d\n",result);
    if(XR_FAILED(result)) { xrDestroyInstance(instance); return 4; }
    XrSystemProperties sp{XR_TYPE_SYSTEM_PROPERTIES}; xrGetSystemProperties(instance,system,&sp);
    printf("headset=%s orientationTracking=%u positionTracking=%u\n",sp.systemName,sp.trackingProperties.orientationTracking,sp.trackingProperties.positionTracking);
    PFN_xrGetD3D11GraphicsRequirementsKHR requirementsFn{};
    result=xrGetInstanceProcAddr(instance,"xrGetD3D11GraphicsRequirementsKHR",reinterpret_cast<PFN_xrVoidFunction*>(&requirementsFn));
    if(XR_FAILED(result)) { xrDestroyInstance(instance); return 5; }
    XrGraphicsRequirementsD3D11KHR requirements{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
    result=requirementsFn(instance,system,&requirements);
    printf("xrGetD3D11GraphicsRequirementsKHR=%d\n",result);
    if(XR_FAILED(result)) { xrDestroyInstance(instance); return 5; }
    ComPtr<IDXGIFactory1> factory;
    CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    ComPtr<IDXGIAdapter1> adapter;
    if(factory) for(UINT i=0;factory->EnumAdapters1(i,&adapter)==S_OK;++i) {
        DXGI_ADAPTER_DESC1 desc{}; adapter->GetDesc1(&desc);
        if(!memcmp(&desc.AdapterLuid,&requirements.adapterLuid,sizeof(LUID))) break;
        adapter.Reset();
    }
    if(!adapter) { puts("Required graphics adapter not found"); xrDestroyInstance(instance); return 6; }
    ComPtr<ID3D11Device> device;
    D3D_FEATURE_LEVEL selected{};
    HRESULT hr=D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,&selected,nullptr);
    printf("D3D11CreateDevice=0x%08lx featureLevel=0x%x minimum=0x%x\n",hr,selected,requirements.minFeatureLevel);
    if(FAILED(hr) || selected<requirements.minFeatureLevel) { xrDestroyInstance(instance); return 6; }
    XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR}; binding.device=device.Get();
    XrSessionCreateInfo sessionInfo{XR_TYPE_SESSION_CREATE_INFO}; sessionInfo.next=&binding; sessionInfo.systemId=system;
    XrSession session{};
    result=xrCreateSession(instance,&sessionInfo,&session);
    printf("xrCreateSession=%d\n",result);
    if(XR_SUCCEEDED(result)) {
        PFN_xrGetDisplayRefreshRateFB getRate{}; float hz{};
        if(refreshRate && XR_SUCCEEDED(xrGetInstanceProcAddr(instance,"xrGetDisplayRefreshRateFB",reinterpret_cast<PFN_xrVoidFunction*>(&getRate))) &&
           getRate && XR_SUCCEEDED(getRate(session,&hz)) && hz>0) printf("display_refresh_hz=%.2f\n",hz);
        else puts("display_refresh_hz=unavailable (controlled by headset/SteamVR)");
        count=0; xrEnumerateViewConfigurationViews(instance,system,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,0,&count,nullptr);
        std::vector<XrViewConfigurationView> views(count,{XR_TYPE_VIEW_CONFIGURATION_VIEW});
        xrEnumerateViewConfigurationViews(instance,system,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,count,&count,views.data());
        for(uint32_t i=0;i<count;++i) printf("eye%u=%ux%u recommendedMSAA=%u\n",i,views[i].recommendedImageRectWidth,views[i].recommendedImageRectHeight,views[i].recommendedSwapchainSampleCount);
        if(argc==2 && strcmp(argv[1],"--render")==0 && !RenderProbe(instance,system,session,device.Get())) result=XR_ERROR_RUNTIME_FAILURE;
        xrDestroySession(session);
    }
    xrDestroyInstance(instance);
    puts("This probe does not render game stereo or establish headset acceptance.");
    return XR_FAILED(result)?7:0;
}
