#pragma once
#include <windows.h>
#include <d3d11.h>
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <wrl/client.h>
#include <array>
#include <functional>
#include <vector>

// Owns frame resources, not the caller's instance/session/device. No game hooks.
class XrFrames {
public:
    using Logger=void (*)(const char*);
    explicit XrFrames(Logger logger=nullptr):logger_(logger) {}
    XrFrames(const XrFrames&) = delete;
    XrFrames& operator=(const XrFrames&) = delete;
    struct Eye {
        XrSwapchain chain{};
        uint32_t width{},height{};
        std::vector<XrSwapchainImageD3D11KHR> images;
        std::vector<Microsoft::WRL::ComPtr<ID3D11RenderTargetView>> targets;
    };
    using Draw = std::function<void(unsigned,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t)>;
    using Prepare = std::function<void(const std::array<XrView,2>&)>;
    struct Screen {
        XrPosef pose{{0,0,0,1},{0,0,-2}};
        XrExtent2Df size{2.4f,1.8f};
    };
    ~XrFrames();
    bool Initialize(XrInstance instance,XrSystemId system,XrSession session,ID3D11Device* device,float scale);
    bool Tick(const Draw& draw,const Prepare& prepare={},const Screen* screen=nullptr);
    bool Exiting() const { return exiting_; }
    uint64_t Submitted() const { return submitted_; }
    bool Visible() const { return visible_; }
private:
    void Report(const char* format,...);
    bool Check(XrResult result,const char* operation);
    Logger logger_{};
    friend struct XrFramesTestAccess;
    XrInstance instance_{};
    XrSession session_{};
    XrSpace space_{};
    std::array<Eye,2> eyes_;
    bool running_{},exiting_{},visible_{};
    uint64_t submitted_{};
};
