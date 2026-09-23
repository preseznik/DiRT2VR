#include "xr_frames.h"
#include <cstdio>
#include <stdexcept>
#include <string>
#include <vector>
#include <cmath>

// Exercise the real frame loop against a deterministic runtime, without a HMD.
// Initialize's graphics allocation is covered by the live cube test separately.
struct XrFramesTestAccess {
    static void Seed(XrFrames& frames,float fovScale=1.f) {
        frames.fovScale_=fovScale;
        frames.instance_=static_cast<XrInstance>(1);
        frames.session_=static_cast<XrSession>(2);
        frames.space_=static_cast<XrSpace>(3);
        for(unsigned i=0;i<3;++i) {
            frames.eyes_[i].chain=static_cast<XrSwapchain>(10+i);
            frames.eyes_[i].width=frames.eyes_[i].height=1;
            frames.eyes_[i].targets.resize(1);
        }
    }
};
namespace {
std::vector<std::string> calls;
XrSessionState event=XR_SESSION_STATE_READY;
bool render=true, tracking=true, timeoutOnce=false;
bool expectScreen=false;
bool expectHud=false;
int acquireFailure=-1;
uint32_t imageIndex=0, submittedLayers=0;
float expectedFovScale=1.f;
XrFovf renderedFov[2]{};
void Require(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
void Reset() {
    calls.clear(); event=XR_SESSION_STATE_READY; render=tracking=true;
    acquireFailure=-1; imageIndex=submittedLayers=0; timeoutOnce=false; expectScreen=expectHud=false; expectedFovScale=1.f;
}
unsigned Eye(XrSwapchain chain) { return static_cast<unsigned>(static_cast<uint64_t>(chain)-10); }
}

extern "C" {
XRAPI_ATTR XrResult XRAPI_CALL xrPollEvent(XrInstance,XrEventDataBuffer* buffer) {
    if(event==XR_SESSION_STATE_UNKNOWN) return XR_EVENT_UNAVAILABLE;
    auto state=reinterpret_cast<XrEventDataSessionStateChanged*>(buffer);
    *state={XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED}; state->session=static_cast<XrSession>(2);
    state->state=event; event=XR_SESSION_STATE_UNKNOWN; return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrBeginSession(XrSession,const XrSessionBeginInfo*) { calls.emplace_back("beginSession"); return XR_SUCCESS; }
XRAPI_ATTR XrResult XRAPI_CALL xrEndSession(XrSession) { calls.emplace_back("endSession"); return XR_SUCCESS; }
XRAPI_ATTR XrResult XRAPI_CALL xrWaitFrame(XrSession,const XrFrameWaitInfo*,XrFrameState* state) {
    calls.emplace_back("waitFrame"); state->shouldRender=render; state->predictedDisplayTime=123; return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrBeginFrame(XrSession,const XrFrameBeginInfo*) { calls.emplace_back("beginFrame"); return XR_SUCCESS; }
XRAPI_ATTR XrResult XRAPI_CALL xrLocateViews(XrSession,const XrViewLocateInfo* info,XrViewState* state,uint32_t,uint32_t* count,XrView* views) {
    Require(info->displayTime==123,"must locate predicted poses"); calls.emplace_back("locate");
    *count=2; state->viewStateFlags=tracking ? XR_VIEW_STATE_ORIENTATION_VALID_BIT|XR_VIEW_STATE_POSITION_VALID_BIT : 0;
    for(unsigned i=0;i<2;++i) { views[i].pose.orientation.w=1; views[i].pose.position.x=i ? 0.032f : -0.032f; views[i].fov={-.9f,.7f,.8f,-.6f}; }
    return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrAcquireSwapchainImage(XrSwapchain chain,const XrSwapchainImageAcquireInfo*,uint32_t* index) {
    calls.emplace_back("acquire"+std::to_string(Eye(chain))); *index=imageIndex;
    return static_cast<int>(Eye(chain))==acquireFailure ? XR_ERROR_RUNTIME_FAILURE : XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrWaitSwapchainImage(XrSwapchain chain,const XrSwapchainImageWaitInfo*) {
    calls.emplace_back("wait"+std::to_string(Eye(chain)));
    if(timeoutOnce) { timeoutOnce=false; return XR_TIMEOUT_EXPIRED; }
    return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrReleaseSwapchainImage(XrSwapchain chain,const XrSwapchainImageReleaseInfo*) {
    calls.emplace_back("release"+std::to_string(Eye(chain))); return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrEndFrame(XrSession,const XrFrameEndInfo* info) {
    calls.emplace_back("endFrame"); submittedLayers=info->layerCount;
    Require(info->displayTime==123,"must submit at predicted time");
    if(info->layerCount) {
        Require(info->layerCount==(expectHud && !expectScreen ? 2u : 1u),"HUD must be an additional layer only in cockpit mode");
        if(expectHud && !expectScreen) {
            const auto* hud=reinterpret_cast<const XrCompositionLayerQuad*>(info->layers[1]);
            Require(hud->type==XR_TYPE_COMPOSITION_LAYER_QUAD && hud->space==static_cast<XrSpace>(3),"HUD must be a separate LOCAL quad");
            Require(hud->subImage.swapchain==static_cast<XrSwapchain>(12) && hud->eyeVisibility==XR_EYE_VISIBILITY_BOTH,"HUD must have a distinct image visible in both eyes");
            Require(hud->pose.position.z==-4 && hud->size.width==4,"distant HUD placement changed");
            Require(hud->layerFlags==(XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT|XR_COMPOSITION_LAYER_UNPREMULTIPLIED_ALPHA_BIT),"HUD transparency flags missing");
        }
        if(expectScreen) {
            Require(info->layers[0]->type==XR_TYPE_COMPOSITION_LAYER_QUAD,"screen must be a quad");
            auto layer=reinterpret_cast<const XrCompositionLayerQuad*>(info->layers[0]);
            Require(layer->space==static_cast<XrSpace>(3) && layer->eyeVisibility==XR_EYE_VISIBILITY_BOTH,"screen must be stationary in LOCAL space and visible to both eyes");
            Require(layer->pose.position.z==-2 && layer->size.width==2.4f,"screen placement changed");
            Require(layer->subImage.swapchain==static_cast<XrSwapchain>(10),"screen must use the acquired image");
            return XR_SUCCESS;
        }
        auto layer=reinterpret_cast<const XrCompositionLayerProjection*>(info->layers[0]);
        Require(layer->viewCount==2,"must submit both eyes together");
        Require(layer->views[0].pose.position.x<0 && layer->views[1].pose.position.x>0,"must preserve individual eye poses");
        for(unsigned eye=0;eye<2;++eye) {
            const auto& f=layer->views[eye].fov; const auto& drawn=renderedFov[eye];
            Require(f.angleLeft==drawn.angleLeft && f.angleRight==drawn.angleRight && f.angleUp==drawn.angleUp && f.angleDown==drawn.angleDown,"submission must use exactly the rendered cropped FOV");
        }
    }
    return XR_SUCCESS;
}
XRAPI_ATTR XrResult XRAPI_CALL xrDestroySwapchain(XrSwapchain) { return XR_SUCCESS; }
XRAPI_ATTR XrResult XRAPI_CALL xrDestroySpace(XrSpace) { return XR_SUCCESS; }
// Unused initialization entry points are deliberately not a graphics simulation.
XRAPI_ATTR XrResult XRAPI_CALL xrEnumerateViewConfigurationViews(XrInstance,XrSystemId,XrViewConfigurationType,uint32_t,uint32_t*,XrViewConfigurationView*) { return XR_ERROR_RUNTIME_FAILURE; }
XRAPI_ATTR XrResult XRAPI_CALL xrEnumerateSwapchainFormats(XrSession,uint32_t,uint32_t*,int64_t*) { return XR_ERROR_RUNTIME_FAILURE; }
XRAPI_ATTR XrResult XRAPI_CALL xrCreateReferenceSpace(XrSession,const XrReferenceSpaceCreateInfo*,XrSpace*) { return XR_ERROR_RUNTIME_FAILURE; }
XRAPI_ATTR XrResult XRAPI_CALL xrCreateSwapchain(XrSession,const XrSwapchainCreateInfo*,XrSwapchain*) { return XR_ERROR_RUNTIME_FAILURE; }
XRAPI_ATTR XrResult XRAPI_CALL xrEnumerateSwapchainImages(XrSwapchain,uint32_t,uint32_t*,XrSwapchainImageBaseHeader*) { return XR_ERROR_RUNTIME_FAILURE; }
}

int main() {
    try {
        const auto draw=[](unsigned eye,const XrView& view,ID3D11RenderTargetView*,uint32_t,uint32_t) {
            calls.emplace_back("draw"+std::to_string(eye)); renderedFov[eye]=view.fov;
            const float scale=expectScreen ? 1.f : expectedFovScale;
            Require(std::abs(std::tan(view.fov.angleLeft)-std::tan(-.9f)*scale)<.00001f &&
                    std::abs(std::tan(view.fov.angleRight)-std::tan(.7f)*scale)<.00001f &&
                    std::abs(std::tan(view.fov.angleUp)-std::tan(.8f)*scale)<.00001f &&
                    std::abs(std::tan(view.fov.angleDown)-std::tan(-.6f)*scale)<.00001f,"asymmetric FOV crop incorrect");
        };
        Reset();
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(frames.Tick(draw),"normal stereo frame failed");
            Require(frames.Submitted()==1 && submittedLayers==1,"stereo submission not counted");
            Require(calls==std::vector<std::string>{"beginSession","waitFrame","beginFrame","locate","acquire0","wait0","draw0","release0","acquire1","wait1","draw1","release1","endFrame"},"frame lifecycle order changed");
            calls.clear(); render=false;
            Require(!frames.Tick(draw) && submittedLayers==0,"shouldRender=false must submit no layer");
            Require(calls==std::vector<std::string>{"waitFrame","beginFrame","endFrame"},"hidden frame must not acquire images");
            calls.clear(); render=true; tracking=false;
            Require(!frames.Tick(draw) && submittedLayers==0,"invalid tracking must submit no layer");
            Require(calls==std::vector<std::string>{"waitFrame","beginFrame","locate","endFrame"},"invalid tracking must not render");
            calls.clear(); event=XR_SESSION_STATE_STOPPING;
            Require(!frames.Tick(draw),"stopping must not render");
            Require(calls==std::vector<std::string>{"endSession"},"stopping lifecycle incorrect");
        }
        Reset(); timeoutOnce=true;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(frames.Tick(draw),"timeout must wait again before using an image");
            Require(calls[5]=="wait0" && calls[6]=="wait0" && calls[7]=="draw0","timeout must not expose an unwaited image");
        }
        Reset(); acquireFailure=1;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(!frames.Tick(draw) && submittedLayers==0 && frames.Submitted()==0,"one-eye failure must not submit stereo");
            Require(calls[calls.size()-2]=="acquire1" && calls.back()=="endFrame","acquire failure must end the frame");
        }
        Reset();
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(!frames.Tick([](unsigned,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t) { throw std::runtime_error("draw failed"); }),"draw failure must not submit");
            Require(frames.Exiting() && submittedLayers==0,"draw failure must stop cleanly");
            Require(calls[calls.size()-2]=="release0" && calls.back()=="endFrame","draw failure must release its image");
        }
        Reset(); imageIndex=99;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(!frames.Tick(draw) && frames.Exiting() && submittedLayers==0,"invalid image index must fail safely");
        }
        Reset();
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(frames.Tick(draw,[](const std::array<XrView,2>& views) {
                Require(views[0].pose.position.x<0 && views[1].pose.position.x>0,"prepare must receive both eye poses");
                calls.emplace_back("prepare");
            }),"prepared stereo frame failed");
            Require(calls[4]=="prepare" && calls[5]=="acquire0","prepare must precede either eye acquisition");
        }
        Reset();
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            Require(!frames.Tick(draw,[](const std::array<XrView,2>&) { throw std::runtime_error("invalid pose"); }),"failed preparation must not render");
            Require(frames.Exiting() && submittedLayers==0 && calls.back()=="endFrame","preparation failure must end without a layer");
        }
        Reset(); expectScreen=true;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames); XrFrames::Screen screen;
            Require(frames.Tick(draw,{},&screen),"screen frame failed");
            Require(calls==std::vector<std::string>{"beginSession","waitFrame","beginFrame","locate","acquire0","wait0","draw0","release0","endFrame"},"screen must acquire, draw and release only one image");
            calls.clear(); expectScreen=false;
            Require(frames.Tick(draw),"screen to stereo transition failed");
            calls.clear(); expectScreen=true;
            Require(frames.Tick(draw,{},&screen),"stereo to screen transition failed");
            render=false; calls.clear();
            Require(!frames.Tick(draw,{},&screen) && submittedLayers==0,"hidden screen must not submit");
        }
        Reset(); expectScreen=true;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames); XrFrames::Screen screen;
            Require(!frames.Tick([](unsigned,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t) { throw std::runtime_error("screen copy failed"); },{},&screen),"failed screen must not submit");
            Require(frames.Exiting() && submittedLayers==0 && calls[calls.size()-2]=="release0","failed screen must release its image");
        }
        Reset(); expectedFovScale=.8f;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames,.8f);
            Require(frames.Tick(draw),"cropped stereo frame failed");
            expectScreen=true; XrFrames::Screen screen;
            Require(frames.Tick(draw,{},&screen),"cropping must not narrow menu FOV");
            expectScreen=false;
            Require(frames.Tick(draw),"crop must return on resume");
        }
        Reset(); expectHud=true;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            XrFrames::Overlay hud; hud.pose.position.z=-4; hud.size={4,2.25f};
            hud.draw=[](unsigned eye,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t) { Require(eye==2,"HUD image must not reuse an eye"); calls.emplace_back("hud"); };
            Require(frames.Tick(draw,{},nullptr,&hud) && submittedLayers==2,"HUD stereo composition failed");
            Require(calls[calls.size()-4]=="wait2" && calls[calls.size()-3]=="hud" && calls[calls.size()-2]=="release2","HUD swapchain ordering incorrect");
            expectScreen=true; XrFrames::Screen screen;
            Require(frames.Tick(draw,{},&screen,&hud) && submittedLayers==1,"menus must suppress cockpit HUD");
            expectScreen=false; acquireFailure=2;
            Require(!frames.Tick(draw,{},nullptr,&hud) && submittedLayers==0,"failed HUD acquire must end frame without stale composition");
        }
        Reset(); expectHud=true;
        {
            XrFrames frames; XrFramesTestAccess::Seed(frames);
            XrFrames::Overlay hud;
            hud.draw=[](unsigned,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t) { throw std::runtime_error("HUD copy failed"); };
            Require(!frames.Tick(draw,{},nullptr,&hud),"failed HUD draw must not submit");
            Require(frames.Exiting() && submittedLayers==0 && calls[calls.size()-2]=="release2","failed HUD must release its own image");
        }
        puts("OpenXR frame lifecycle tests passed"); return 0;
    } catch(const std::exception& e) { fprintf(stderr,"FAIL: %s\n",e.what()); return 1; }
}
