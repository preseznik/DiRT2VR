#include "xr_frames.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <cstdarg>

void XrFrames::Report(const char* format,...) {
    char message[512]{}; va_list args; va_start(args,format);
    vsnprintf(message,sizeof(message),format,args); va_end(args);
    if(logger_) logger_(message); else puts(message);
}
bool XrFrames::Check(XrResult result,const char* operation) {
    if(XR_FAILED(result)) { Report("%s=%d",operation,result); return false; }
    return true;
}
XrFrames::~XrFrames() {
    for(auto& eye:eyes_) {
        eye.targets.clear(); eye.images.clear();
        if(eye.chain) xrDestroySwapchain(eye.chain);
    }
    if(space_) xrDestroySpace(space_);
}
bool XrFrames::Initialize(XrInstance instance,XrSystemId system,XrSession session,ID3D11Device* device,float scale) {
    if(instance_ || !device || !std::isfinite(scale) || scale<0.25f || scale>1.f) return false;
    instance_=instance; session_=session;
    uint32_t count{};
    if(!Check(xrEnumerateViewConfigurationViews(instance,system,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,0,&count,nullptr),"enumerate views")) return false;
    if(count!=2) { Report("unsupported stereo view count=%u",count); return false; }
    std::array<XrViewConfigurationView,2> views{{{XR_TYPE_VIEW_CONFIGURATION_VIEW},{XR_TYPE_VIEW_CONFIGURATION_VIEW}}};
    if(!Check(xrEnumerateViewConfigurationViews(instance,system,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,2,&count,views.data()),"enumerate views")) return false;
    if(!Check(xrEnumerateSwapchainFormats(session,0,&count,nullptr),"enumerate formats")) return false;
    std::vector<int64_t> formats(count);
    if(!Check(xrEnumerateSwapchainFormats(session,count,&count,formats.data()),"enumerate formats")) return false;
    int64_t format=0;
    for(auto value:formats) Report("runtime_swapchain_format=%lld",static_cast<long long>(value));
    for(auto candidate:{DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
                        DXGI_FORMAT_R8G8B8A8_UNORM,DXGI_FORMAT_B8G8R8A8_UNORM})
        if(std::find(formats.begin(),formats.end(),candidate)!=formats.end()) { format=candidate; break; }
    if(!format) { Report("No supported RGBA/BGRA swapchain format"); return false; }
    XrReferenceSpaceCreateInfo reference{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
    reference.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_LOCAL; reference.poseInReferenceSpace.orientation.w=1;
    if(!Check(xrCreateReferenceSpace(session,&reference,&space_),"create LOCAL space")) return false;
    for(unsigned i=0;i<2;++i) {
        auto& eye=eyes_[i];
        eye.width=std::max(1u,static_cast<uint32_t>(views[i].recommendedImageRectWidth*scale));
        eye.height=std::max(1u,static_cast<uint32_t>(views[i].recommendedImageRectHeight*scale));
        XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};
        info.usageFlags=XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT|XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;
        info.format=format; info.sampleCount=1; info.width=eye.width; info.height=eye.height;
        info.faceCount=1; info.arraySize=1; info.mipCount=1;
        if(!Check(xrCreateSwapchain(session,&info,&eye.chain),"create eye swapchain")) return false;
        if(!Check(xrEnumerateSwapchainImages(eye.chain,0,&count,nullptr),"enumerate images")) return false;
        eye.images.resize(count,{XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
        if(!Check(xrEnumerateSwapchainImages(eye.chain,count,&count,reinterpret_cast<XrSwapchainImageBaseHeader*>(eye.images.data())),"enumerate images")) return false;
        eye.targets.resize(count);
        for(unsigned j=0;j<count;++j) {
            D3D11_RENDER_TARGET_VIEW_DESC target{};
            target.Format=static_cast<DXGI_FORMAT>(format); target.ViewDimension=D3D11_RTV_DIMENSION_TEXTURE2D;
            auto hr=device->CreateRenderTargetView(eye.images[j].texture,&target,&eye.targets[j]);
            if(FAILED(hr)) { Report("CreateRenderTargetView=0x%08lx",hr); return false; }
        }
        Report("swapchain eye=%u %ux%u images=%u",i,eye.width,eye.height,count);
    }
    return true;
}
bool XrFrames::Tick(const Draw& draw,const Prepare& prepare) {
    XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
    XrResult eventResult{};
    while((eventResult=xrPollEvent(instance_,&event))==XR_SUCCESS) {
        if(event.type==XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED) {
            const auto& state=*reinterpret_cast<XrEventDataSessionStateChanged*>(&event);
            if(state.session!=session_) { event={XR_TYPE_EVENT_DATA_BUFFER}; continue; }
            Report("session_state=%d",state.state);
            visible_=state.state==XR_SESSION_STATE_VISIBLE || state.state==XR_SESSION_STATE_FOCUSED;
            if(state.state==XR_SESSION_STATE_READY) {
                XrSessionBeginInfo begin{XR_TYPE_SESSION_BEGIN_INFO}; begin.primaryViewConfigurationType=XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                running_=Check(xrBeginSession(session_,&begin),"xrBeginSession");
                if(!running_) exiting_=true;
            } else if(state.state==XR_SESSION_STATE_STOPPING) {
                if(running_) xrEndSession(session_);
                running_=false;
            } else if(state.state==XR_SESSION_STATE_EXITING || state.state==XR_SESSION_STATE_LOSS_PENDING) exiting_=true;
        } else if(event.type==XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING) exiting_=true;
        event={XR_TYPE_EVENT_DATA_BUFFER};
    }
    if(XR_FAILED(eventResult)) { exiting_=true; return false; }
    if(!running_ || exiting_) return false;
    XrFrameWaitInfo wait{XR_TYPE_FRAME_WAIT_INFO};
    XrFrameState frame{XR_TYPE_FRAME_STATE};
    if(!Check(xrWaitFrame(session_,&wait,&frame),"xrWaitFrame")) { exiting_=true; return false; }
    XrFrameBeginInfo begin{XR_TYPE_FRAME_BEGIN_INFO};
    if(!Check(xrBeginFrame(session_,&begin),"xrBeginFrame")) { exiting_=true; return false; }
    XrFrameEndInfo end{XR_TYPE_FRAME_END_INFO};
    end.displayTime=frame.predictedDisplayTime; end.environmentBlendMode=XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
    std::array<XrView,2> views{{{XR_TYPE_VIEW},{XR_TYPE_VIEW}}};
    XrViewLocateInfo locate{XR_TYPE_VIEW_LOCATE_INFO};
    locate.viewConfigurationType=XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
    locate.displayTime=frame.predictedDisplayTime; locate.space=space_;
    XrViewState state{XR_TYPE_VIEW_STATE}; uint32_t count{};
    bool valid=frame.shouldRender && Check(xrLocateViews(session_,&locate,&state,2,&count,views.data()),"xrLocateViews") && count==2;
    constexpr auto required=XR_VIEW_STATE_ORIENTATION_VALID_BIT|XR_VIEW_STATE_POSITION_VALID_BIT;
    valid=valid && (state.viewStateFlags&required)==required;
    if(valid && prepare) {
        try { prepare(views); }
        catch(const std::exception& error) { valid=false; exiting_=true; Report("frame preparation failed: %s",error.what()); }
        catch(...) { valid=false; exiting_=true; Report("frame preparation failed: unknown exception"); }
    }
    std::array<XrCompositionLayerProjectionView,2> projectionViews{{{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW},{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW}}};
    for(unsigned i=0;valid && i<2;++i) {
        auto& eye=eyes_[i]; uint32_t index{};
        XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
        if(!Check(xrAcquireSwapchainImage(eye.chain,&acquire,&index),"acquire image")) { valid=false; break; }
        XrSwapchainImageWaitInfo imageWait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO}; imageWait.timeout=XR_INFINITE_DURATION;
        XrResult waited;
        do { waited=xrWaitSwapchainImage(eye.chain,&imageWait); } while(waited==XR_TIMEOUT_EXPIRED);
        if(!Check(waited,"wait image")) { valid=false; exiting_=true; break; }
        try {
            if(index>=eye.targets.size()) throw std::out_of_range("OpenXR image index");
            draw(i,views[i],eye.targets[index].Get(),eye.width,eye.height);
        }
        catch(const std::exception& error) { valid=false; exiting_=true; Report("eye %u failed: %s",i,error.what()); }
        catch(...) { valid=false; exiting_=true; Report("eye %u failed: unknown exception",i); }
        XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
        if(!Check(xrReleaseSwapchainImage(eye.chain,&release),"release image")) { valid=false; exiting_=true; }
        projectionViews[i].pose=views[i].pose; projectionViews[i].fov=views[i].fov;
        projectionViews[i].subImage.swapchain=eye.chain;
        projectionViews[i].subImage.imageRect.extent={static_cast<int32_t>(eye.width),static_cast<int32_t>(eye.height)};
    }
    XrCompositionLayerProjection projection{XR_TYPE_COMPOSITION_LAYER_PROJECTION};
    projection.space=space_; projection.viewCount=2; projection.views=projectionViews.data();
    const XrCompositionLayerBaseHeader* layers[]={reinterpret_cast<const XrCompositionLayerBaseHeader*>(&projection)};
    if(valid) { end.layerCount=1; end.layers=layers; }
    if(!Check(xrEndFrame(session_,&end),"xrEndFrame")) { exiting_=true; return false; }
    if(valid) ++submitted_;
    return valid;
}
