// DiRT2VR photo viewer: shows saved Photo Mode pictures in the headset. A side-by-side (-SBS) photo
// gives each eye its own half with the field of view it was taken with (from the -SBS.json beside it),
// so it is seen in 3D at its real size. Other photos are shown on a large screen in front of you.
// Usage: photo_viewer.exe --list <UTF-8 file, one photo path per line> [--index N] [--log <file>]
#include <windows.h>
#include <shellapi.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wincodec.h>
#include <xinput.h>
#include <wrl/client.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <share.h>
#include <string>
#include <vector>
#include "xr_adapter.h"
using Microsoft::WRL::ComPtr;

namespace {
FILE* logFile{};
void Log(const char* format,...) {
    char text[1024]{};va_list args;va_start(args,format);vsnprintf(text,sizeof text,format,args);va_end(args);
    OutputDebugStringA(text);OutputDebugStringA("\n");
    if(logFile){fprintf(logFile,"%s\n",text);fflush(logFile);}
}
bool Check(XrResult result,const char* what){if(XR_FAILED(result))Log("%s failed: %d",what,int(result));return XR_SUCCEEDED(result);}

// ---- Photos ---------------------------------------------------------------------------------------
struct Photo {
    std::wstring path;
    bool stereo{};
    UINT width{},height{};          // The whole picture.
    std::vector<unsigned char> bgra; // Top-down rows.
    float tangents[2][4]{};          // Per eye: tan(left), tan(right), tan(up), tan(down) as in OpenXR.
    float eyeDistance{.063f};
    bool viewFromFile{};
};
bool EndsWith(const std::wstring& text,const wchar_t* tail) {
    const size_t n=wcslen(tail);
    return text.size()>=n && _wcsicmp(text.c_str()+text.size()-n,tail)==0;
}
bool ReadView(const std::wstring& photo,Photo& out) {
    std::wstring path=photo;path.replace(path.size()-4,4,L".json");
    std::ifstream file(path,std::ios::binary);
    if(!file)return false;
    std::string text((std::istreambuf_iterator<char>(file)),std::istreambuf_iterator<char>());
    const auto numbers=[&](const char* key,float* values,int count) {
        auto at=text.find(key);if(at==std::string::npos)return false;
        at=text.find_first_of("[0123456789-",at+strlen(key));if(at==std::string::npos)return false;
        if(text[at]=='[')++at;
        const char* p=text.c_str()+at;
        for(int i=0;i<count;++i) {
            char* end{};values[i]=std::strtof(p,&end);if(end==p || !std::isfinite(values[i]))return false;
            p=end;while(*p==',' || *p==' ')++p;
        }
        return true;
    };
    float distance{};
    if(!numbers("\"left\"",out.tangents[0],4) || !numbers("\"right\"",out.tangents[1],4) || !numbers("\"eyeDistance\"",&distance,1))return false;
    for(auto& eye:out.tangents)if(!(eye[0]<0 && eye[1]>0 && eye[2]>0 && eye[3]<0))return false;
    out.eyeDistance=std::clamp(distance,.02f,.2f);
    return true;
}
bool LoadPhoto(IWICImagingFactory* wic,const std::wstring& path,Photo& out) {
    out={};out.path=path;
    ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICFormatConverter> converter;
    if(FAILED(wic->CreateDecoderFromFilename(path.c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnDemand,&decoder)) ||
       FAILED(decoder->GetFrame(0,&frame)) || FAILED(wic->CreateFormatConverter(&converter)) ||
       FAILED(converter->Initialize(frame.Get(),GUID_WICPixelFormat32bppBGRA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom)) ||
       FAILED(converter->GetSize(&out.width,&out.height)) || !out.width || !out.height || out.width>16384 || out.height>16384)return false;
    out.bgra.resize(size_t(out.width)*out.height*4);
    if(FAILED(converter->CopyPixels(nullptr,out.width*4,UINT(out.bgra.size()),out.bgra.data())))return false;
    out.stereo=EndsWith(path,L"-SBS.png") && out.width>=2;
    const float aspect=float(out.width/(out.stereo?2:1))/float(out.height);
    if(out.stereo && ReadView(path,out))out.viewFromFile=true;
    else for(auto& eye:out.tangents){eye[0]=-1;eye[1]=1;eye[2]=1/aspect;eye[3]=-1/aspect;} // 90 degrees across.
    return true;
}

// ---- Window -----------------------------------------------------------------------------------------
struct State {
    std::vector<std::wstring> paths;
    int index{};
    Photo photo;
    bool loaded{},changed{true},recenter{true},quit{};
    std::wstring status=L"Starting VR…";
    HWND window{};
} state;
void Step(int direction) {
    if(state.paths.empty())return;
    state.index=(state.index+direction+int(state.paths.size()))%int(state.paths.size());
    state.changed=true;
}
LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    switch(message) {
    case WM_KEYDOWN:
        if(w==VK_LEFT || w==VK_PRIOR || w==VK_UP)Step(-1);
        else if(w==VK_RIGHT || w==VK_NEXT || w==VK_DOWN || w==VK_SPACE)Step(1);
        else if(w=='R')state.recenter=true;
        else if(w==VK_ESCAPE)DestroyWindow(window);
        return 0;
    case WM_PAINT: {
        PAINTSTRUCT paint{};HDC dc=BeginPaint(window,&paint);
        RECT client{};GetClientRect(window,&client);
        FillRect(dc,&client,static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
        const int textHeight=86;
        if(state.loaded && !state.photo.bgra.empty()) {
            // Preview: the left eye of a side-by-side photo, or the whole picture.
            const int sourceWidth=int(state.photo.width/(state.photo.stereo?2:1)),sourceHeight=int(state.photo.height);
            const int areaWidth=client.right,areaHeight=std::max(1,int(client.bottom)-textHeight);
            const float scale=std::min(float(areaWidth)/sourceWidth,float(areaHeight)/sourceHeight);
            const int shownWidth=int(sourceWidth*scale),shownHeight=int(sourceHeight*scale);
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof info.bmiHeader;info.bmiHeader.biWidth=int(state.photo.width);
            info.bmiHeader.biHeight=-int(state.photo.height);info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            SetStretchBltMode(dc,HALFTONE);
            StretchDIBits(dc,(areaWidth-shownWidth)/2,(areaHeight-shownHeight)/2,shownWidth,shownHeight,0,0,sourceWidth,sourceHeight,state.photo.bgra.data(),&info,DIB_RGB_COLORS,SRCCOPY);
        }
        SetBkMode(dc,TRANSPARENT);SetTextColor(dc,RGB(235,238,240));
        HFONT font=CreateFontW(-18,0,0,0,FW_NORMAL,0,0,0,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");
        auto old=SelectObject(dc,font);
        std::wstring name=state.paths.empty()?L"":state.paths[state.index];
        name=name.substr(name.find_last_of(L"\\/")+1);
        wchar_t line[512]{};
        swprintf_s(line,L"%d / %d   %s%s",state.index+1,int(state.paths.size()),name.c_str(),
            !state.loaded?L"":state.photo.stereo?L"   ·   3D (side by side)":L"   ·   flat");
        RECT text{12,int(client.bottom)-textHeight+8,client.right-12,client.bottom};
        DrawTextW(dc,line,-1,&text,DT_LEFT|DT_SINGLELINE|DT_END_ELLIPSIS);
        text.top+=26;DrawTextW(dc,state.status.c_str(),-1,&text,DT_LEFT|DT_SINGLELINE|DT_END_ELLIPSIS);
        text.top+=26;SetTextColor(dc,RGB(150,156,162));
        DrawTextW(dc,L"← / →  previous / next     R  recenter     Esc  close     Controller: D-pad, Y recenter, B close",-1,&text,DT_LEFT|DT_SINGLELINE|DT_END_ELLIPSIS);
        SelectObject(dc,old);DeleteObject(font);
        EndPaint(window,&paint);
        return 0;
    }
    case WM_SIZE: InvalidateRect(window,nullptr,FALSE);return 0;
    case WM_DESTROY: state.quit=true;PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(window,message,w,l);
}

// ---- Controller --------------------------------------------------------------------------------------
using XInputGetStateFn=DWORD (WINAPI*)(DWORD,XINPUT_STATE*);
XInputGetStateFn xinput{};
WORD previousButtons{};
void PollController() {
    if(!xinput)return;
    WORD buttons{};
    for(DWORD i=0;i<4;++i){XINPUT_STATE pad{};if(xinput(i,&pad)==ERROR_SUCCESS){buttons=pad.Gamepad.wButtons;break;}}
    const WORD pressed=buttons&~previousButtons;previousButtons=buttons;
    if(pressed&(XINPUT_GAMEPAD_DPAD_LEFT|XINPUT_GAMEPAD_LEFT_SHOULDER))Step(-1);
    if(pressed&(XINPUT_GAMEPAD_DPAD_RIGHT|XINPUT_GAMEPAD_RIGHT_SHOULDER|XINPUT_GAMEPAD_A))Step(1);
    if(pressed&XINPUT_GAMEPAD_Y)state.recenter=true;
    if(pressed&(XINPUT_GAMEPAD_B|XINPUT_GAMEPAD_BACK) && state.window)PostMessageW(state.window,WM_CLOSE,0,0);
}

// ---- Pose helpers ------------------------------------------------------------------------------------
XrVector3f Rotate(const XrQuaternionf& q,const XrVector3f& v) {
    const XrVector3f u{q.x,q.y,q.z};const float s=q.w;
    const float dot=u.x*v.x+u.y*v.y+u.z*v.z,uu=u.x*u.x+u.y*u.y+u.z*u.z;
    const XrVector3f cross{u.y*v.z-u.z*v.y,u.z*v.x-u.x*v.z,u.x*v.y-u.y*v.x};
    return {2*dot*u.x+(s*s-uu)*v.x+2*s*cross.x,2*dot*u.y+(s*s-uu)*v.y+2*s*cross.y,2*dot*u.z+(s*s-uu)*v.z+2*s*cross.z};
}
// Where you look, kept level: the photo is placed straight ahead of it.
XrPosef LevelPose(const XrPosef& head) {
    const XrVector3f forward=Rotate(head.orientation,{0,0,-1});
    const float yaw=std::atan2(-forward.x,-forward.z);
    return {{0,std::sin(yaw/2),0,std::cos(yaw/2)},head.position};
}

// ---- OpenXR ------------------------------------------------------------------------------------------
struct Image { XrSwapchain chain{}; int32_t width{},height{}; };
struct Xr {
    XrInstance instance{};XrSystemId system{};XrSession session{};XrSpace local{},view{};
    ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
    int64_t format{};bool rgba{},srgb{};
    std::array<Image,2> images;unsigned imageCount{};
    bool running{},exiting{};XrPosef anchor{{0,0,0,1},{0,0,0}};bool anchored{};
} xr;
std::wstring Start() {
    const char* extensions[]={XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
    XrInstanceCreateInfo create{XR_TYPE_INSTANCE_CREATE_INFO};
    strcpy_s(create.applicationInfo.applicationName,"DiRT2VR photo viewer");
    create.applicationInfo.apiVersion=XR_MAKE_VERSION(1,0,0);
    create.enabledExtensionCount=1;create.enabledExtensionNames=extensions;
    if(!Check(xrCreateInstance(&create,&xr.instance),"xrCreateInstance"))return L"The VR runtime could not be started. Start your headset software and try again.";
    XrSystemGetInfo get{XR_TYPE_SYSTEM_GET_INFO};get.formFactor=XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
    if(!Check(xrGetSystem(xr.instance,&get,&xr.system),"xrGetSystem"))return L"No headset found. Connect your headset and try again.";
    PFN_xrGetD3D11GraphicsRequirementsKHR requirementsFn{};
    XrGraphicsRequirementsD3D11KHR requirements{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
    if(!Check(xrGetInstanceProcAddr(xr.instance,"xrGetD3D11GraphicsRequirementsKHR",reinterpret_cast<PFN_xrVoidFunction*>(&requirementsFn)),"requirements function") ||
       !Check(requirementsFn(xr.instance,xr.system,&requirements),"graphics requirements"))return L"The VR runtime does not support Direct3D 11.";
    ComPtr<IDXGIFactory1> factory;CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    ComPtr<IDXGIAdapter1> adapter;
    if(factory)for(UINT i=0;factory->EnumAdapters1(i,&adapter)==S_OK;++i) {
        DXGI_ADAPTER_DESC1 desc{};adapter->GetDesc1(&desc);
        if(RequiredAdapter(requirements.adapterLuid,desc.AdapterLuid))break;
        adapter.Reset();
    }
    if(!adapter)return L"The graphics card the headset needs was not found.";
    D3D_FEATURE_LEVEL level{};
    if(FAILED(D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&xr.device,&level,&xr.context)) ||
       level<requirements.minFeatureLevel)return L"Direct3D 11 could not be started on the headset's graphics card.";
    XrGraphicsBindingD3D11KHR binding{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR};binding.device=xr.device.Get();
    XrSessionCreateInfo session{XR_TYPE_SESSION_CREATE_INFO};session.next=&binding;session.systemId=xr.system;
    if(!Check(xrCreateSession(xr.instance,&session,&xr.session),"xrCreateSession"))return L"The VR session could not be started.";
    XrReferenceSpaceCreateInfo space{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};space.poseInReferenceSpace={{0,0,0,1},{0,0,0}};
    space.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_LOCAL;
    if(!Check(xrCreateReferenceSpace(xr.session,&space,&xr.local),"local space"))return L"The VR session could not be started.";
    space.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_VIEW;
    if(!Check(xrCreateReferenceSpace(xr.session,&space,&xr.view),"view space"))return L"The VR session could not be started.";
    uint32_t count{};xrEnumerateSwapchainFormats(xr.session,0,&count,nullptr);
    std::vector<int64_t> formats(count);xrEnumerateSwapchainFormats(xr.session,count,&count,formats.data());
    // sRGB first: the photo's pixels are sRGB-encoded.
    for(const int64_t wanted:{int64_t(DXGI_FORMAT_R8G8B8A8_UNORM_SRGB),int64_t(DXGI_FORMAT_B8G8R8A8_UNORM_SRGB),int64_t(DXGI_FORMAT_R8G8B8A8_UNORM),int64_t(DXGI_FORMAT_B8G8R8A8_UNORM)})
        if(!xr.format && std::find(formats.begin(),formats.end(),wanted)!=formats.end())xr.format=wanted;
    if(!xr.format)return L"The VR runtime offers no usable image format.";
    xr.rgba=xr.format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB || xr.format==DXGI_FORMAT_R8G8B8A8_UNORM;
    xr.srgb=xr.format==DXGI_FORMAT_R8G8B8A8_UNORM_SRGB || xr.format==DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
    Log("session ready: format=%lld",static_cast<long long>(xr.format));
    return {};
}
void DestroyImages() {
    for(auto& image:xr.images)if(image.chain){xrDestroySwapchain(image.chain);image={};}
    xr.imageCount=0;
}
// The current photo into one swapchain per eye (side by side) or one for both (flat).
bool Upload(const Photo& photo) {
    DestroyImages();
    xr.imageCount=photo.stereo?2:1;
    const int32_t width=int32_t(photo.width/xr.imageCount),height=int32_t(photo.height);
    std::vector<unsigned char> rows(size_t(width)*height*4);
    static unsigned char linear[256];static bool table{};
    if(!table){for(int i=0;i<256;++i){const float c=i/255.f;linear[i]=static_cast<unsigned char>(std::lround(255*(c<=.04045f?c/12.92f:std::pow((c+.055f)/1.055f,2.4f))));}table=true;}
    for(unsigned eye=0;eye<xr.imageCount;++eye) {
        auto& image=xr.images[eye];image.width=width;image.height=height;
        XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
        info.usageFlags=XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT|XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT|XR_SWAPCHAIN_USAGE_SAMPLED_BIT;
        info.format=xr.format;info.sampleCount=1;info.width=uint32_t(width);info.height=uint32_t(height);info.faceCount=1;info.arraySize=1;info.mipCount=1;
        if(!Check(xrCreateSwapchain(xr.session,&info,&image.chain),"xrCreateSwapchain"))return false;
        uint32_t count{};xrEnumerateSwapchainImages(image.chain,0,&count,nullptr);
        std::vector<XrSwapchainImageD3D11KHR> textures(count,{XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
        if(!Check(xrEnumerateSwapchainImages(image.chain,count,&count,reinterpret_cast<XrSwapchainImageBaseHeader*>(textures.data())),"swapchain images"))return false;
        for(int32_t y=0;y<height;++y) {
            const unsigned char* in=photo.bgra.data()+(size_t(y)*photo.width+size_t(eye)*width)*4;
            unsigned char* out=rows.data()+size_t(y)*width*4;
            for(int32_t x=0;x<width;++x,in+=4,out+=4) {
                unsigned char b=in[0],g=in[1],r=in[2];
                if(!xr.srgb){b=linear[b];g=linear[g];r=linear[r];} // A linear format shows sRGB values too bright otherwise.
                out[0]=xr.rgba?r:b;out[1]=g;out[2]=xr.rgba?b:r;out[3]=255;
            }
        }
        // Fill every image of the chain once; afterwards the last one released keeps being shown.
        for(uint32_t i=0;i<count;++i) {
            uint32_t index{};XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
            if(!Check(xrAcquireSwapchainImage(image.chain,&acquire,&index),"acquire"))return false;
            XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};wait.timeout=XR_INFINITE_DURATION;
            if(!Check(xrWaitSwapchainImage(image.chain,&wait),"wait image"))return false;
            xr.context->UpdateSubresource(textures[index].texture,0,nullptr,rows.data(),UINT(width*4),0);
            XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
            if(!Check(xrReleaseSwapchainImage(image.chain,&release),"release"))return false;
        }
    }
    xr.context->Flush();
    return true;
}
void PollEvents() {
    XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
    while(xrPollEvent(xr.instance,&event)==XR_SUCCESS) {
        if(event.type==XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) {
            const auto& changed=*reinterpret_cast<XrEventDataSessionStateChanged*>(&event);
            Log("session state=%d",int(changed.state));
            if(changed.state==XR_SESSION_STATE_READY) {
                XrSessionBeginInfo begin{XR_TYPE_SESSION_BEGIN_INFO};begin.primaryViewConfigurationType=XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                xr.running=Check(xrBeginSession(xr.session,&begin),"xrBeginSession");
                state.status=L"Showing in the headset.";InvalidateRect(state.window,nullptr,FALSE);
            } else if(changed.state==XR_SESSION_STATE_STOPPING) {
                xrEndSession(xr.session);xr.running=false;
            } else if(changed.state==XR_SESSION_STATE_EXITING || changed.state==XR_SESSION_STATE_LOSS_PENDING)xr.exiting=true;
        } else if(event.type==XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING)xr.exiting=true;
        event={XR_TYPE_EVENT_DATA_BUFFER};
    }
}
void Frame(IWICImagingFactory* wic) {
    XrFrameState frame{XR_TYPE_FRAME_STATE};
    if(!Check(xrWaitFrame(xr.session,nullptr,&frame),"xrWaitFrame") || !Check(xrBeginFrame(xr.session,nullptr),"xrBeginFrame")){xr.exiting=true;return;}
    XrSpaceLocation head{XR_TYPE_SPACE_LOCATION};
    if(state.recenter && XR_SUCCEEDED(xrLocateSpace(xr.view,xr.local,frame.predictedDisplayTime,&head)) &&
       (head.locationFlags&XR_SPACE_LOCATION_ORIENTATION_VALID_BIT) && (head.locationFlags&XR_SPACE_LOCATION_POSITION_VALID_BIT)) {
        xr.anchor=LevelPose(head.pose);xr.anchored=true;state.recenter=false;
    }
    if(state.changed) {
        state.changed=false;
        state.loaded=!state.paths.empty() && LoadPhoto(wic,state.paths[state.index],state.photo) && Upload(state.photo);
        state.recenter=true; // Each photo straight ahead.
        state.status=state.loaded?(state.photo.stereo && !state.photo.viewFromFile?L"Showing in the headset (field of view not saved with this photo: shown at 90°).":L"Showing in the headset."):
            L"This photo could not be shown.";
        Log("photo %d loaded=%d stereo=%d size=%ux%u view_from_file=%d",state.index,state.loaded,state.photo.stereo,state.photo.width,state.photo.height,state.photo.viewFromFile);
        InvalidateRect(state.window,nullptr,FALSE);
    }
    XrCompositionLayerProjection projection{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
    std::array<XrCompositionLayerProjectionView,2> views{{{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW},{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}}};
    XrCompositionLayerQuad quad{XR_TYPE_COMPOSITION_LAYER_QUAD};
    const XrCompositionLayerBaseHeader* layers[1]{};uint32_t layerCount{};
    if(frame.shouldRender && state.loaded && xr.anchored) {
        if(state.photo.stereo) {
            // Each eye as it was taken: its own picture, field of view and place, fixed in front of you.
            for(unsigned eye=0;eye<2;++eye) {
                auto& v=views[eye];
                const float side=(eye?.5f:-.5f)*state.photo.eyeDistance;
                const XrVector3f offset=Rotate(xr.anchor.orientation,{side,0,0});
                v.pose={xr.anchor.orientation,{xr.anchor.position.x+offset.x,xr.anchor.position.y+offset.y,xr.anchor.position.z+offset.z}};
                const auto& t=state.photo.tangents[eye];
                v.fov={std::atan(t[0]),std::atan(t[1]),std::atan(t[2]),std::atan(t[3])};
                v.subImage.swapchain=xr.images[eye].chain;v.subImage.imageRect={{0,0},{xr.images[eye].width,xr.images[eye].height}};
            }
            projection.space=xr.local;projection.viewCount=2;projection.views=views.data();
            layers[layerCount++]=reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection);
        } else {
            // A flat photo: a large screen 2 m in front of you.
            const float width=2.6f,height=width*float(state.photo.height)/float(state.photo.width);
            const XrVector3f ahead=Rotate(xr.anchor.orientation,{0,0,-2});
            quad.space=xr.local;quad.eyeVisibility=XR_EYE_VISIBILITY_BOTH;
            quad.pose={xr.anchor.orientation,{xr.anchor.position.x+ahead.x,xr.anchor.position.y+ahead.y,xr.anchor.position.z+ahead.z}};
            quad.size={width,height};quad.subImage.swapchain=xr.images[0].chain;
            quad.subImage.imageRect={{0,0},{xr.images[0].width,xr.images[0].height}};
            layers[layerCount++]=reinterpret_cast<const XrCompositionLayerBaseHeader*>(&quad);
        }
    }
    XrFrameEndInfo end{XR_TYPE_FRAME_END_INFO};end.displayTime=frame.predictedDisplayTime;
    end.environmentBlendMode=XR_ENVIRONMENT_BLEND_MODE_OPAQUE;end.layerCount=layerCount;end.layers=layers;
    if(!Check(xrEndFrame(xr.session,&end),"xrEndFrame"))xr.exiting=true;
}
std::vector<std::wstring> ReadList(const std::wstring& path) {
    std::vector<std::wstring> paths;
    std::ifstream file(path,std::ios::binary);
    std::string line;
    while(std::getline(file,line)) {
        while(!line.empty() && (line.back()=='\r' || line.back()=='\n'))line.pop_back();
        if(line.size()>=3 && (unsigned char)line[0]==0xEF && (unsigned char)line[1]==0xBB && (unsigned char)line[2]==0xBF)line.erase(0,3);
        if(line.empty())continue;
        std::wstring wide(size_t(MultiByteToWideChar(CP_UTF8,0,line.data(),int(line.size()),nullptr,0)),L'\0');
        MultiByteToWideChar(CP_UTF8,0,line.data(),int(line.size()),wide.data(),int(wide.size()));
        if(EndsWith(wide,L".png") && paths.size()<4096)paths.push_back(wide);
    }
    return paths;
}
}

int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int show) {
    int argc{};LPWSTR* argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    std::wstring list;
    for(int i=1;i+1<argc;++i) {
        if(!wcscmp(argv[i],L"--list"))list=argv[++i];
        else if(!wcscmp(argv[i],L"--index"))state.index=_wtoi(argv[++i]);
        else if(!wcscmp(argv[i],L"--log"))logFile=_wfsopen(argv[++i],L"w",_SH_DENYWR); // Readable while it runs.
    }
    LocalFree(argv);
    state.paths=ReadList(list);
    if(state.paths.empty()){MessageBoxW(nullptr,L"No photos to show.",L"DiRT2VR photos",MB_ICONINFORMATION);return 1;}
    state.index=std::clamp(state.index,0,int(state.paths.size())-1);
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    ComPtr<IWICImagingFactory> wic;
    if(FAILED(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&wic)))) return 2;
    if(auto module=LoadLibraryExW(L"xinput1_4.dll",nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32))
        xinput=reinterpret_cast<XInputGetStateFn>(GetProcAddress(module,"XInputGetState"));
    WNDCLASSW type{};type.lpfnWndProc=WindowProc;type.hInstance=instance;type.lpszClassName=L"DiRT2VRPhotoViewer";
    type.hCursor=LoadCursor(nullptr,IDC_ARROW);type.hIcon=LoadIcon(nullptr,IDI_APPLICATION);
    RegisterClassW(&type);
    state.window=CreateWindowExW(0,type.lpszClassName,L"DiRT2VR photos in VR",WS_OVERLAPPEDWINDOW,CW_USEDEFAULT,CW_USEDEFAULT,900,600,nullptr,nullptr,instance,nullptr);
    ShowWindow(state.window,show);UpdateWindow(state.window);
    // Show the first photo in the window even before (or without) the headset.
    state.loaded=LoadPhoto(wic.Get(),state.paths[state.index],state.photo);InvalidateRect(state.window,nullptr,FALSE);
    const std::wstring failure=Start();
    if(!failure.empty()) {
        state.status=failure;InvalidateRect(state.window,nullptr,FALSE);
        MessageBoxW(state.window,failure.c_str(),L"DiRT2VR photos",MB_ICONWARNING);
        DestroyWindow(state.window);
    }
    MSG message{};
    while(!state.quit) {
        while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
            if(message.message==WM_QUIT)state.quit=true;
            TranslateMessage(&message);DispatchMessageW(&message);
        }
        if(state.quit || !xr.session)break;
        PollController();
        PollEvents();
        if(xr.exiting)break;
        if(xr.running)Frame(wic.Get());
        else Sleep(10);
    }
    if(xr.running){xrRequestExitSession(xr.session);}
    DestroyImages();
    if(xr.view)xrDestroySpace(xr.view);
    if(xr.local)xrDestroySpace(xr.local);
    if(xr.session)xrDestroySession(xr.session);
    if(xr.instance)xrDestroyInstance(xr.instance);
    if(state.window && IsWindow(state.window))DestroyWindow(state.window);
    if(logFile)fclose(logFile);
    return failure.empty()?0:3;
}
