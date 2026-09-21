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
    XrFrames() = default;
    XrFrames(const XrFrames&) = delete;
    XrFrames& operator=(const XrFrames&) = delete;
    struct Eye {
        XrSwapchain chain{};
        uint32_t width{},height{};
        std::vector<XrSwapchainImageD3D11KHR> images;
        std::vector<Microsoft::WRL::ComPtr<ID3D11RenderTargetView>> targets;
    };
    using Draw = std::function<void(unsigned,const XrView&,ID3D11RenderTargetView*,uint32_t,uint32_t)>;
    ~XrFrames();
    bool Initialize(XrInstance instance,XrSystemId system,XrSession session,ID3D11Device* device,float scale);
    bool Tick(const Draw& draw);
    bool Exiting() const { return exiting_; }
    uint64_t Submitted() const { return submitted_; }
    bool Visible() const { return visible_; }
private:
    friend struct XrFramesTestAccess;
    XrInstance instance_{};
    XrSession session_{};
    XrSpace space_{};
    std::array<Eye,2> eyes_;
    bool running_{},exiting_{},visible_{};
    uint64_t submitted_{};
};
